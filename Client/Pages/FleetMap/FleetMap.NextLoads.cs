using System.Text.Json;
using Client.Models.DTO.Planning;
using Client.Services;
using Microsoft.JSInterop;

namespace Client.Pages.FleetMap;

public partial class FleetMap
{
  private static readonly JsonSerializerOptions MapJsonOptions = new(
    JsonSerializerDefaults.Web
  );
  private bool ShowNextLoads { get; set; }
  private int _nextLoadsVersion;
  private string? _nextLoadsMessage;
  private string? _nextLoadsRevision;

  // The truck's position is polled every ten seconds and upcoming loads used
  // to be asked about on the same beat. They change when a dispatcher changes
  // them, and answering "nothing changed" costs the server about a dozen
  // database round trips - most of a second, six times a minute, for every
  // map left open on a truck. Anything this user does asks at once; only the
  // idle beat waits. A road or an empty drive still being prepared keeps the
  // fast beat, so it appears as soon as it is ready.
  internal static readonly TimeSpan NextLoadsPollInterval =
    TimeSpan.FromSeconds(30);
  private DateTimeOffset _nextLoadsCheckedAt;
  private (
    Guid Truck,
    Guid? Dispatch,
    Guid? ExecutionLeg,
    long AssignmentRevision
  )? _nextLoadsIdentity;
  private Guid? _planningExecutionLegId;
  private long _planningAssignmentRevision;
  private Guid? SelectedExecutionLegId =>
    _planningExecutionLegId ?? _routeState?.Plan?.ExecutionLegId;
  private long SelectedAssignmentRevision =>
    _planningExecutionLegId.HasValue
      ? _planningAssignmentRevision
      : _routeState?.Plan?.AssignmentRevision ?? 0;
  private CancellationTokenSource? _nextLoadsRequest;
  private readonly NextLoadDisplayCache _nextLoadsCache = new();
  private IReadOnlyList<NextLoadRoute> _nextLoadRoutes = [];

  private void ResetNextLoads()
  {
    ++_nextLoadsVersion;
    _nextLoadsRequest?.Cancel();
    _nextLoadsIdentity = null;
    _nextLoadsRevision = null;
    _nextLoadsMessage = null;
    _nextLoadRoutes = [];
    ResetInspectedLoad();
  }

  private async Task OnNextLoadsChanged()
  {
    ++_nextLoadsVersion;
    _nextLoadsRequest?.Cancel();
    _nextLoadsMessage = null;
    if (!ShowNextLoads)
      ResetInspectedLoad();
    await SaveMapPreferencesAsync();
    if (_map is null || _disposed)
      return;
    await _map.InvokeVoidAsync("setNextLoadsVisible", ShowNextLoads);
    await RefreshNextLoadsAsync();
  }

  private async Task RefreshNextLoadsAsync(bool polled = false)
  {
    if (
      _map is null
      || _disposed
      || !ShowNextLoads
      || _activeTruckId is not { } truckId
    )
      return;
    var currentId = SelectedDispatchId;
    var executionLegId = SelectedExecutionLegId;
    var assignmentRevision = SelectedAssignmentRevision;
    var identity = (truckId, currentId, executionLegId, assignmentRevision);
    if (
      polled
      && _nextLoadsIdentity == identity
      && _nextLoadsRevision is not null
      && _nextLoadRoutes.All(route =>
        route.Status != "pending" && route.Deadhead is not null
      )
      && Clock.GetUtcNow() - _nextLoadsCheckedAt < NextLoadsPollInterval
    )
      return;
    if (
      _nextLoadsRequest is { IsCancellationRequested: false }
      && _nextLoadsIdentity == identity
    )
      return;
    _nextLoadsRequest?.Cancel();
    var version = ++_nextLoadsVersion;
    using var request = CancellationTokenSource.CreateLinkedTokenSource(
      _lifetime.Token
    );
    _nextLoadsRequest = request;
    try
    {
      if (_nextLoadsIdentity != identity)
      {
        _nextLoadRoutes = [];
        ResetInspectedLoad();
        _nextLoadsIdentity = identity;
        _nextLoadsRevision = null;
        await _map.InvokeVoidAsync("clearNextLoads");
        if (!IsCurrentNextLoads(version, identity))
          return;
        if (
          currentId is { } dispatchId
          && _nextLoadsCache.Get(
            (truckId, dispatchId),
            Clock.GetUtcNow(),
            executionLegId,
            assignmentRevision
          )
            is { } cached
        )
        {
          if (!IsCurrentNextLoads(version, identity))
            return;
          using var savedJson = JsonDocument.Parse(cached.Payload);
          RememberNextLoadRoutes(
            savedJson
              .RootElement.GetProperty("routes")
              .Deserialize<List<NextLoadRoute>>(MapJsonOptions) ?? []
          );
          await _map.InvokeVoidAsync("setNextLoadsBytes", cached.Payload);
          if (!IsCurrentNextLoads(version, identity))
            return;
          _nextLoadsRevision = cached.Revision;
        }
      }
      if (currentId is null)
      {
        _nextLoadsMessage = null;
        return;
      }
      if (!IsCurrentNextLoads(version, identity))
        return;
      var legQuery = executionLegId is { } legId
        ? $"&currentExecutionLegId={legId}"
        : "";
      var response = await Api.GetAsync<NextLoadRoutesResponse>(
        $"api/dispatch/truck/{truckId}/next-routes"
          + $"?currentDispatchId={currentId}{legQuery}"
          + $"&revision={Uri.EscapeDataString(_nextLoadsRevision ?? "")}",
        request.Token
      );
      if (!IsCurrentNextLoads(version, identity))
        return;
      if (!response.Success || response.Response is null)
      {
        _nextLoadsMessage = "Next load routes could not be loaded.";
        return;
      }
      _nextLoadsMessage = null;
      _nextLoadsCheckedAt = Clock.GetUtcNow();
      if (response.Response.Unchanged)
      {
        if (
          _nextLoadsCache.Get(
            (truckId, currentId.Value),
            Clock.GetUtcNow(),
            executionLegId,
            assignmentRevision
          )
            is { } saved
          && saved.Revision == response.Response.Revision
        )
          _nextLoadsCache.Store(
            (truckId, currentId.Value),
            saved.Revision,
            saved.Payload,
            Clock.GetUtcNow(),
            executionLegId,
            assignmentRevision
          );
        return;
      }
      if (response.Response.Routes is null && response.Response.Labels is null)
        return;
      var upcoming = response
        .Response.Routes?.Where(x =>
          x.Id != currentId || x.ExecutionLegId != executionLegId
        )
        .ToList();
      if (
        response.Response.Labels?.Any(label =>
          !(upcoming ?? _nextLoadRoutes).Any(route =>
            route.Id == label.Id && route.ExecutionLegId == label.ExecutionLegId
          )
        ) == true
      )
        return;
      using var buffer = new ResponsiveWriteStream();
      await JsonSerializer.SerializeAsync(
        buffer,
        new
        {
          Routes = upcoming,
          response.Response.Labels,
          TruckId = truckId,
          CurrentDispatchId = currentId,
          CurrentExecutionLegId = executionLegId,
          CurrentAssignmentRevision = assignmentRevision,
        },
        MapJsonOptions,
        request.Token
      );
      var payload = buffer.ToArray();
      var snapshot = upcoming is not null
        ? payload
        : await MergeNextLoadLabelsAsync(
          truckId,
          currentId.Value,
          response.Response.Labels,
          request.Token,
          executionLegId,
          assignmentRevision
        );
      if (IsCurrentNextLoads(version, identity))
      {
        if (upcoming is not null)
          RememberNextLoadRoutes(upcoming);
        await _map.InvokeVoidAsync("setNextLoadsBytes", payload);
        if (IsCurrentNextLoads(version, identity))
        {
          _nextLoadsRevision = response.Response.Revision;
          if (snapshot is not null)
            _nextLoadsCache.Store(
              (truckId, currentId.Value),
              response.Response.Revision,
              snapshot,
              Clock.GetUtcNow(),
              executionLegId,
              assignmentRevision
            );
        }
      }
    }
    catch (OperationCanceledException) when (request.IsCancellationRequested)
    { }
    finally
    {
      if (ReferenceEquals(_nextLoadsRequest, request))
        _nextLoadsRequest = null;
    }
  }

  private bool IsCurrentNextLoads(
    int version,
    (
      Guid Truck,
      Guid? Dispatch,
      Guid? ExecutionLeg,
      long AssignmentRevision
    ) identity
  ) =>
    !_disposed
    && version == _nextLoadsVersion
    && identity.Truck == _activeTruckId
    && identity.Dispatch == SelectedDispatchId
    && identity.ExecutionLeg == SelectedExecutionLegId
    && identity.AssignmentRevision == SelectedAssignmentRevision;

  private async Task<byte[]?> MergeNextLoadLabelsAsync(
    Guid truckId,
    Guid dispatchId,
    IReadOnlyList<NextLoadLabels>? labels,
    CancellationToken cancellationToken,
    Guid? executionLegId,
    long assignmentRevision
  )
  {
    if (
      _nextLoadsCache.Get(
        (truckId, dispatchId),
        Clock.GetUtcNow(),
        executionLegId,
        assignmentRevision
      )
      is not { } cached
    )
      return null;
    using var json = JsonDocument.Parse(cached.Payload);
    using var buffer = new ResponsiveWriteStream();
    // Cache complete geometry, but keep the live interop update metadata-only.
    await JsonSerializer.SerializeAsync(
      buffer,
      new
      {
        Routes = json.RootElement.GetProperty("routes"),
        Labels = labels,
        TruckId = truckId,
        CurrentDispatchId = dispatchId,
        CurrentExecutionLegId = executionLegId,
        CurrentAssignmentRevision = assignmentRevision,
      },
      MapJsonOptions,
      cancellationToken
    );
    return buffer.ToArray();
  }

  private void RememberNextLoadRoutes(IReadOnlyList<NextLoadRoute> routes)
  {
    _nextLoadRoutes = routes
      .Select(route =>
        route with
        {
          Legs = route
            .Legs.Select(leg => leg with { Points = [], Path = null })
            .ToArray(),
          Deadhead = route.Deadhead is { } deadhead
            ? deadhead with
            {
              Points = [],
              Path = null,
            }
            : null,
        }
      )
      .ToArray();
    if (
      _inspectedLoadId is { } id
      && !_nextLoadRoutes.Any(route =>
        route.Id == id && route.ExecutionLegId == _inspectedExecutionLegId
      )
    )
      ResetInspectedLoad();
  }
}
