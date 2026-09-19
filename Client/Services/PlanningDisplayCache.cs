using Client.Models.DTO.Planning;

namespace Client.Services;

public sealed class PlanningDisplayCache(ApiService api, TimeProvider? timeProvider = null)
{
  private const int MaximumEntries = 100;
  private const long MaximumGeometryUnits = 262_144;
  private readonly TimeProvider clock = timeProvider ?? TimeProvider.System;
  private readonly Dictionary<string, (AutomaticPlanningResult Result, DateTimeOffset Expires, long GeometryUnits, string? ETag)> entries = [];
  private readonly Dictionary<string, Task<Client.Models.DTO.RequestResponseDTO<AutomaticPlanningResult>>> refreshing = [];

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
      if (Overflow || written.Contains(url)) return;
      if (written.Count < 256) written.Add(url);
      else { Overflow = true; written.Clear(); }
    }
  }

  public void Clear() { entries.Clear(); geometryUnits = 0; refreshing.Clear(); previews.Clear(); preloadRead = null; loadedAt = default; generation++; preload = null; }

  public async Task<AutomaticPlanningResult?> ReadPreviewAsync(Guid truckId, CancellationToken ct)
  {
    var version = generation;
    var url = $"api/fleet/trucks/{truckId}/planning";
    var pending = new PreviewRead(url);
    previews.Add(pending);
    try
    {
      var response = await api.GetAsync<AutomaticPlanningResult>($"{url}/preview", ct);
      if (ct.IsCancellationRequested || version != generation) return null;
      // Track only active reads, so newer no-plan responses need no persistent tombstones.
      if (pending.Superseded) return Get(url);
      if (!response.Success || response.Response is not { } result || result.TruckId != truckId
        || result.State?.Plan?.GeometryOmitted == true) return null;
      Store(url, result);
      return result;
    }
    finally { previews.Remove(pending); }
  }

  public async Task<Client.Models.DTO.RequestResponseDTO<AutomaticPlanningResult>> RefreshAsync(string url, CancellationToken ct, bool force = false)
  {
    ct.ThrowIfCancellationRequested();
    if (force || !refreshing.TryGetValue(url, out var running))
    {
      var completion = new TaskCompletionSource<Client.Models.DTO.RequestResponseDTO<AutomaticPlanningResult>>(TaskCreationOptions.RunContinuationsAsynchronously);
      refreshing[url] = running = completion.Task;
      _ = CompleteRefreshAsync(url, completion);
    }
    return await running.WaitAsync(ct);
  }

  private async Task CompleteRefreshAsync(string url, TaskCompletionSource<Client.Models.DTO.RequestResponseDTO<AutomaticPlanningResult>> completion)
  {
    try
    {
      var response = await RefreshCoreAsync(url, completion.Task, CancellationToken.None);
      ReleaseRefresh(url, completion.Task);
      completion.TrySetResult(response);
    }
    catch (Exception ex)
    {
      ReleaseRefresh(url, completion.Task);
      completion.TrySetException(ex);
    }
  }

  private void ReleaseRefresh(string url, Task<Client.Models.DTO.RequestResponseDTO<AutomaticPlanningResult>> completed)
  {
    // Awaiters can immediately start the next refresh when completion is published.
    if (refreshing.TryGetValue(url, out var running) && ReferenceEquals(running, completed)) refreshing.Remove(url);
  }

  private async Task<Client.Models.DTO.RequestResponseDTO<AutomaticPlanningResult>> RefreshCoreAsync(string url,
    Task<Client.Models.DTO.RequestResponseDTO<AutomaticPlanningResult>> owner, CancellationToken ct)
  {
    var version = generation;
    var cached = Get(url);
    var previous = cached?.State?.Plan;
    var requestUrl = previous is null ? url : $"{url}?knownPlanId={previous.Id}&knownVersion={previous.Version}";
    var response = await api.GetAsync<AutomaticPlanningResult>(requestUrl, cached is null ? null : Tag(url), ct);
    if (response.NotModified)
    {
      // The server confirmed the tagged reply is current; a copy evicted meanwhile is read again in full.
      if (cached is not null && Get(url) is not null) response.Response = cached;
      else response = await api.GetAsync<AutomaticPlanningResult>(url, null, ct);
    }
    if (response.Response?.State?.Plan is { GeometryOmitted: true } plan)
    {
      if (previous is null || plan.Id != previous.Id || plan.Version != previous.Version
        || plan.Route.Legs.Count != previous.Route.Legs.Count
        || plan.ReferenceRoute?.Legs.Count != previous.ReferenceRoute?.Legs.Count)
        response = await api.GetAsync<AutomaticPlanningResult>(url, null, ct);
      else
      {
        plan.Route.Points = previous.Route.Points;
        plan.Route.Legs = plan.Route.Legs.Select((leg, index) => leg with { Points = previous.Route.Legs[index].Points }).ToList();
        if (plan.ReferenceRoute is { } reference && previous.ReferenceRoute is { } saved)
        {
          reference.Points = saved.Points;
          reference.Legs = reference.Legs.Select((leg, index) => leg with { Points = saved.Legs[index].Points }).ToList();
        }
        plan.GeometryOmitted = false;
      }
    }
    if (response.Success && response.Response is { State.Eta: { RouteUpdatePending: false, Stops.Count: > 0 } eta } result
      && result.State.Plan?.Tracking.AllStopsPassed != true
      && eta.ValidUntil.ToUniversalTime() > eta.CalculatedAt.ToUniversalTime()
      && eta.ValidUntil.ToUniversalTime() <= clock.GetUtcNow().UtcDateTime)
    {
      // A reply that expired in transit cannot replace a complete display or renew its grace deadline.
      response.Response = result with { State = result.State with { Eta = eta with { RouteUpdatePending = true } } };
    }
    if (response.Success && version == generation && !ct.IsCancellationRequested
      && refreshing.TryGetValue(url, out var current) && ReferenceEquals(current, owner)) Store(url, response.Response, response.ETag);
    return response;
  }

  public Task PreloadAsync()
  {
    if (clock.GetUtcNow() - loadedAt < TimeSpan.FromMinutes(2)) return Task.CompletedTask;
    if (preload is { IsCompleted: false }) return preload;
    return preload = LoadAsync(generation);
  }

  private async Task LoadAsync(int version)
  {
    var pending = new PreloadRead();
    preloadRead = pending;
    try
    {
      var response = await api.GetAsync<List<AutomaticPlanningResult>>("api/fleet/planning/previews");
      // Heavy write churn discards this optional snapshot instead of growing invalidation history.
      if (version != generation || pending.Overflow || !response.Success || response.Response is null) return;
      if (ReferenceEquals(preloadRead, pending)) preloadRead = null;
      loadedAt = clock.GetUtcNow();
      foreach (var result in response.Response)
      {
        var truckUrl = $"api/fleet/trucks/{result.TruckId}/planning";
        var dispatchUrl = $"api/dispatch/{result.DispatchId}/planning/automatic";
        if (pending.Allows(truckUrl) && !previews.Any(x => x.Url == truckUrl) && Get(truckUrl) is null) Store(truckUrl, result);
        if (pending.Allows(dispatchUrl) && !previews.Any(x => x.Url == dispatchUrl) && Get(dispatchUrl) is null) Store(dispatchUrl, result);
      }
    }
    finally { if (ReferenceEquals(preloadRead, pending)) preloadRead = null; }
  }

  public AutomaticPlanningResult? Get(string url)
  {
    if (!entries.TryGetValue(url, out var entry)) return null;
    if (entry.Expires > clock.GetUtcNow()) return entry.Result;
    Remove(url);
    return null;
  }

  public void StoreRecalculated(AutomaticPlanningResult result)
  {
    var keys = entries.Where(x => result.DispatchId.HasValue && x.Value.Result.DispatchId == result.DispatchId)
      .Select(x => x.Key).ToHashSet();
    keys.Add($"api/fleet/trucks/{result.TruckId}/planning");
    if (result.DispatchId is { } dispatchId) keys.Add($"api/dispatch/{dispatchId}/planning/automatic");
    foreach (var key in keys)
    {
      // Revoke only this plan's in-flight reads; other trucks keep their own request ownership.
      refreshing.Remove(key);
      Store(key, result);
    }
  }

  public string? Tag(string url) => entries.TryGetValue(url, out var entry) ? entry.ETag : null;

  public void Store(string url, AutomaticPlanningResult? result, string? etag = null)
  {
    preloadRead?.Record(url);
    foreach (var preview in previews.Where(x => x.Url == url)) preview.Superseded = true;
    var now = clock.GetUtcNow();
    foreach (var key in entries.Where(x => x.Value.Expires <= now).Select(x => x.Key).ToArray())
      Remove(key);
    Remove(url);
    if (result?.State?.Plan is not { } plan) return;
    var weight = GeometryWeight(plan);
    if (weight > MaximumGeometryUnits) return;
    while (entries.Count >= MaximumEntries || geometryUnits + weight > MaximumGeometryUnits)
      Remove(entries.MinBy(x => x.Value.Expires).Key);
    entries[url] = (result, now.AddMinutes(5), weight, etag);
    geometryUnits += weight;
  }

  private void Remove(string url)
  {
    if (entries.Remove(url, out var entry)) geometryUnits -= entry.GeometryUnits;
  }

  private static long GeometryWeight(RoutePlan plan) => 128L + RouteWeight(plan.Route)
    + RouteWeight(plan.ReferenceRoute) + 4L * (plan.Stops.Count + (long)(plan.ReferenceStops?.Count ?? 0));

  private static long RouteWeight(TruckRoute? route)
  {
    if (route is null) return 0;
    // Coordinate units conservatively count duplicate lists and structural overhead, not heap bytes.
    var weight = 32L + route.Points.Count;
    foreach (var leg in route.Legs)
    {
      weight += 16L + leg.Points.Count;
      if (weight > MaximumGeometryUnits) break;
    }
    return weight;
  }
}
