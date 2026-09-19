using Client.Models.DTO;
using Client.Models.DTO.Planning;

namespace Client.Services;

public sealed class PlanningDisplayCache(
  ApiService api,
  TimeProvider? timeProvider = null
) : IDisposable
{
  private const int MaximumEntries = 100;
  private const long MaximumGeometryUnits = 262_144;
  private readonly TimeProvider clock = timeProvider ?? TimeProvider.System;
  private readonly Dictionary<
    string,
    (AutomaticPlanningResult Result, DateTimeOffset Expires, long GeometryUnits)
  > entries = [];
  private readonly Dictionary<string, RefreshRead> refreshing = [];

  private CancellationTokenSource lifetime = new();
  private bool disposed;

  private sealed class RefreshRead(CancellationToken lifetime)
  {
    public CancellationTokenSource Cancellation { get; } =
      CancellationTokenSource.CreateLinkedTokenSource(lifetime);
    public TaskCompletionSource<
      RequestResponseDTO<AutomaticPlanningResult>
    > Completion { get; } =
      new(TaskCreationOptions.RunContinuationsAsynchronously);
    public int Waiters { get; set; }
  }

  private Task? preload;
  private DateTimeOffset loadedAt;
  private long geometryUnits;
  private int generation;
  private readonly HashSet<PreviewRead> previews = [];
  private PreloadRead? preloadRead;

  private sealed class PreviewRead(string url)
  {
    public string Url { get; } = url;
    public bool Superseded { get; set; }
  }

  private sealed class PreloadRead
  {
    private readonly HashSet<string> written = [];
    public bool Overflow { get; private set; }

    public bool Allows(string url) => !Overflow && !written.Contains(url);

    public void Record(string url)
    {
      if (Overflow || written.Contains(url))
        return;
      if (written.Count < 256)
        written.Add(url);
      else
      {
        Overflow = true;
        written.Clear();
      }
    }
  }

  public void Clear()
  {
    if (disposed)
      return;
    var previous = lifetime;
    lifetime = new();
    entries.Clear();
    geometryUnits = 0;
    refreshing.Clear();
    previews.Clear();
    preloadRead = null;
    loadedAt = default;
    generation++;
    preload = null;
    previous.Cancel();
    previous.Dispose();
  }

  public void Dispose()
  {
    if (disposed)
      return;
    Clear();
    disposed = true;
    lifetime.Dispose();
  }

  public void Invalidate(Guid truckId, Guid dispatchId)
  {
    var keys = entries
      .Where(x =>
        x.Value.Result.TruckId == truckId
        || x.Value.Result.DispatchId == dispatchId
      )
      .Select(x => x.Key)
      .ToHashSet();
    keys.Add($"api/fleet/trucks/{truckId}/planning");
    keys.Add($"api/dispatch/{dispatchId}/planning/automatic");
    foreach (var key in keys)
    {
      refreshing.Remove(key);
      Store(key, null);
    }
  }

  public async Task<AutomaticPlanningResult?> ReadPreviewAsync(
    Guid truckId,
    CancellationToken ct
  )
  {
    using var request = CancellationTokenSource.CreateLinkedTokenSource(
      ct,
      lifetime.Token
    );
    var version = generation;
    var url = $"api/fleet/trucks/{truckId}/planning";
    var pending = new PreviewRead(url);
    previews.Add(pending);
    try
    {
      var response = await api.GetAsync<AutomaticPlanningResult>(
        $"{url}/preview",
        request.Token
      );
      if (request.IsCancellationRequested || version != generation)
        return null;
      // Track only active reads, so newer no-plan responses need no persistent
      // tombstones.
      if (pending.Superseded)
        return Get(url);
      if (
        !response.Success
        || response.Response is not { } result
        || result.TruckId != truckId
        || result.State?.Plan?.GeometryOmitted == true
      )
        return null;
      Store(url, result);
      return result;
    }
    finally
    {
      previews.Remove(pending);
    }
  }

  public async Task<RequestResponseDTO<AutomaticPlanningResult>> RefreshAsync(
    string url,
    CancellationToken ct,
    bool force = false
  )
  {
    ct.ThrowIfCancellationRequested();
    if (force || !refreshing.TryGetValue(url, out var running))
    {
      refreshing[url] = running = new(lifetime.Token);
      running.Waiters++;
      _ = CompleteRefreshAsync(url, running);
    }
    else
      running.Waiters++;
    try
    {
      return await running.Completion.Task.WaitAsync(ct);
    }
    finally
    {
      if (--running.Waiters == 0 && !running.Completion.Task.IsCompleted)
      {
        ReleaseRefresh(url, running);
        running.Cancellation.Cancel();
      }
    }
  }

  private async Task CompleteRefreshAsync(string url, RefreshRead read)
  {
    try
    {
      var response = await RefreshCoreAsync(url, read, read.Cancellation.Token);
      ReleaseRefresh(url, read);
      read.Completion.TrySetResult(response);
    }
    catch (Exception ex)
    {
      ReleaseRefresh(url, read);
      read.Completion.TrySetException(ex);
    }
    finally
    {
      read.Cancellation.Dispose();
    }
  }

  private void ReleaseRefresh(string url, RefreshRead completed)
  {
    // Awaiters can immediately start the next refresh when completion is
    // published.
    if (
      refreshing.TryGetValue(url, out var running)
      && ReferenceEquals(running, completed)
    )
      refreshing.Remove(url);
  }

  private async Task<
    RequestResponseDTO<AutomaticPlanningResult>
  > RefreshCoreAsync(string url, RefreshRead owner, CancellationToken ct)
  {
    var version = generation;
    var previous = Get(url)?.State?.Plan;
    var query = url.Contains('?') ? '&' : '?';
    var requestUrl = previous is null
      ? url
      : $"{url}{query}knownPlanId={previous.Id}"
        + $"&knownVersion={previous.Version}";
    var response = await api.PostAsync<object, AutomaticPlanningResult>(
      requestUrl,
      new { },
      ct
    );
    if (response.Response?.State?.Plan is { GeometryOmitted: true } plan)
    {
      if (
        previous is null
        || plan.TruckId != previous.TruckId
        || plan.DispatchId != previous.DispatchId
        || plan.ExecutionLegId != previous.ExecutionLegId
        || plan.AssignmentRevision != previous.AssignmentRevision
        || plan.Id != previous.Id
        || plan.Version != previous.Version
        || plan.Route.Legs.Count != previous.Route.Legs.Count
        || plan.ReferenceRoute?.Legs.Count
          != previous.ReferenceRoute?.Legs.Count
      )
        response = await api.PostAsync<object, AutomaticPlanningResult>(
          url,
          new { },
          ct
        );
      else
      {
        plan.Route.Points = previous.Route.Points;
        plan.Route.Legs = plan
          .Route.Legs.Select(
            (leg, index) =>
              leg with
              {
                Points = previous.Route.Legs[index].Points,
              }
          )
          .ToList();
        if (
          plan.ReferenceRoute is { } reference
          && previous.ReferenceRoute is { } saved
        )
        {
          reference.Points = saved.Points;
          reference.Legs = reference
            .Legs.Select(
              (leg, index) => leg with { Points = saved.Legs[index].Points }
            )
            .ToList();
        }
        plan.GeometryOmitted = false;
      }
    }
    if (response.Success)
      response.Response = ForDisplay(
        response.Response,
        clock.GetUtcNow().UtcDateTime
      );
    if (
      response.Success
      && version == generation
      && !ct.IsCancellationRequested
      && refreshing.TryGetValue(url, out var current)
      && ReferenceEquals(current, owner)
    )
      Store(url, response.Response);
    return response;
  }

  public static AutomaticPlanningResult? ForDisplay(
    AutomaticPlanningResult? result,
    DateTime now
  )
  {
    // A reply that expired in transit cannot replace a complete display or
    // renew its grace deadline.
    if (
      result
        is { State.Eta: { RouteUpdatePending: false, Stops.Count: > 0 } eta }
      && result.State.Plan?.Tracking.AllStopsPassed != true
      && eta.ValidUntil.ToUniversalTime() > eta.CalculatedAt.ToUniversalTime()
      && eta.ValidUntil.ToUniversalTime() <= now
    )
      return result with
      {
        State = result.State with
        {
          Eta = eta with { RouteUpdatePending = true },
        },
      };
    return result;
  }

  public Task PreloadAsync()
  {
    if (clock.GetUtcNow() - loadedAt < TimeSpan.FromMinutes(2))
      return Task.CompletedTask;
    if (preload is { IsCompleted: false })
      return preload;
    return preload = LoadAsync(generation);
  }

  private async Task LoadAsync(int version)
  {
    using var request = CancellationTokenSource.CreateLinkedTokenSource(
      lifetime.Token
    );
    var pending = new PreloadRead();
    preloadRead = pending;
    try
    {
      var response = await api.GetAsync<List<AutomaticPlanningResult>>(
        "api/fleet/planning/previews",
        request.Token
      );
      // Heavy write churn discards this optional snapshot instead of growing
      // invalidation history.
      if (
        request.IsCancellationRequested
        || version != generation
        || pending.Overflow
        || !response.Success
        || response.Response is null
      )
        return;
      if (ReferenceEquals(preloadRead, pending))
        preloadRead = null;
      loadedAt = clock.GetUtcNow();
      foreach (var result in response.Response)
      {
        var truckUrl = $"api/fleet/trucks/{result.TruckId}/planning";
        var dispatchUrl =
          $"api/dispatch/{result.DispatchId}/planning/automatic";
        if (
          pending.Allows(truckUrl)
          && !previews.Any(x => x.Url == truckUrl)
          && Get(truckUrl) is null
        )
          Store(truckUrl, result);
        if (
          ExecutionLegId(result) is null
          && pending.Allows(dispatchUrl)
          && !previews.Any(x => x.Url == dispatchUrl)
          && Get(dispatchUrl) is null
        )
          Store(dispatchUrl, result);
      }
    }
    finally
    {
      if (ReferenceEquals(preloadRead, pending))
        preloadRead = null;
    }
  }

  public AutomaticPlanningResult? Get(string url)
  {
    if (!entries.TryGetValue(url, out var entry))
      return null;
    if (entry.Expires > clock.GetUtcNow())
      return entry.Result;
    Remove(url);
    return null;
  }

  public void StoreRecalculated(AutomaticPlanningResult result)
  {
    var keys = entries
      .Where(x =>
        result.DispatchId.HasValue
        && x.Value.Result.DispatchId == result.DispatchId
        && x.Value.Result.TruckId == result.TruckId
        && ExecutionLegId(x.Value.Result) == ExecutionLegId(result)
        && AssignmentRevision(x.Value.Result) == AssignmentRevision(result)
      )
      .Select(x => x.Key)
      .ToHashSet();
    keys.Add($"api/fleet/trucks/{result.TruckId}/planning");
    if (result.DispatchId is { } dispatchId && ExecutionLegId(result) is null)
      keys.Add($"api/dispatch/{dispatchId}/planning/automatic");
    foreach (var key in keys)
    {
      // Revoke only this plan's in-flight reads; other trucks keep their own
      // request ownership.
      refreshing.Remove(key);
      Store(key, result);
    }
  }

  public void Store(string url, AutomaticPlanningResult? result)
  {
    if (
      result is { DispatchId: { } dispatchId }
      && ExecutionLegId(result).HasValue
    )
    {
      var unscoped = $"api/dispatch/{dispatchId}/planning/automatic";
      if (url != unscoped)
      {
        refreshing.Remove(unscoped);
        Store(unscoped, null);
      }
    }
    preloadRead?.Record(url);
    foreach (var preview in previews.Where(x => x.Url == url))
      preview.Superseded = true;
    var now = clock.GetUtcNow();
    foreach (
      var key in entries
        .Where(x => x.Value.Expires <= now)
        .Select(x => x.Key)
        .ToArray()
    )
      Remove(key);
    Remove(url);
    if (result?.State?.Plan is not { } plan)
      return;
    if (
      ExecutionLegId(result).HasValue
      && url == $"api/dispatch/{result.DispatchId}/planning/automatic"
    )
      return;
    var weight = GeometryWeight(plan);
    if (weight > MaximumGeometryUnits)
      return;
    while (
      entries.Count >= MaximumEntries
      || geometryUnits + weight > MaximumGeometryUnits
    )
      Remove(entries.MinBy(x => x.Value.Expires).Key);
    entries[url] = (result, now.AddMinutes(5), weight);
    geometryUnits += weight;
  }

  private static Guid? ExecutionLegId(AutomaticPlanningResult result) =>
    result.ExecutionLegId ?? result.State?.Plan?.ExecutionLegId;

  private static long AssignmentRevision(AutomaticPlanningResult result) =>
    result.State?.Plan?.AssignmentRevision ?? result.AssignmentRevision;

  private void Remove(string url)
  {
    if (entries.Remove(url, out var entry))
      geometryUnits -= entry.GeometryUnits;
  }

  private static long GeometryWeight(RoutePlan plan) =>
    128L
    + RouteWeight(plan.Route)
    + RouteWeight(plan.ReferenceRoute)
    + 4L * (plan.Stops.Count + (long)(plan.ReferenceStops?.Count ?? 0));

  private static long RouteWeight(TruckRoute? route)
  {
    if (route is null)
      return 0;
    // Coordinate units conservatively count duplicate lists and structural
    // overhead, not heap bytes.
    var weight = 32L + route.Points.Count;
    foreach (var leg in route.Legs)
    {
      weight += 16L + leg.Points.Count;
      if (weight > MaximumGeometryUnits)
        break;
    }
    return weight;
  }
}
