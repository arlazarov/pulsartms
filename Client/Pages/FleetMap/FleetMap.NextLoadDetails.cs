using Client.Models.DTO.Dispatch;
using Client.Models.DTO.Planning;
using Microsoft.JSInterop;

namespace Client.Pages.FleetMap;

public partial class FleetMap
{
  private Guid? _inspectedLoadId;
  private Guid? _inspectedExecutionLegId;
  private int _inspectedStopIndex;
  private bool _nextStopDetailsOpen;

  private DispatchResponse? _inspectedDetails;
  private string? _inspectedDetailsError;
  private CancellationTokenSource? _inspectedDetailsRequest;
  private int _inspectedDetailsVersion;
  private readonly Dictionary<
    (Guid Truck, Guid Current, Guid Load),
    (DispatchResponse Details, DateTimeOffset Expires)
  > _inspectedDetailsCache = [];
  private NextLoadRoute? InspectedRoute =>
    _nextLoadRoutes.FirstOrDefault(route =>
      route.Id == _inspectedLoadId
      && route.ExecutionLegId == _inspectedExecutionLegId
    );
  private FuelStopArrival? InspectedFuelArrival =>
    _routeState is { } state
      ? state.FuelStopArrivals.FirstOrDefault(x =>
        x.DispatchId == _inspectedLoadId && x.StopId == InspectedStop?.Id
      )
      : null;
  private bool InspectedEtaRefreshing =>
    _etaRefreshPending
    || _routeState?.Eta?.RouteUpdatePending == true
    || _inspectedLoadId is { } id
      && _routeState?.Eta?.PendingDispatches?.ContainsKey(id) == true;
  private double? InspectedDistanceMiles
  {
    get
    {
      if (RemainingMiles is not { } remaining || !double.IsFinite(remaining))
        return null;
      var total = Math.Max(0, remaining);
      foreach (var route in _nextLoadRoutes)
      {
        if (route.Deadhead is not { } empty || !double.IsFinite(empty.Miles))
          return null;
        total += empty.Miles;
        var legCount =
          route == InspectedRoute
            ? _inspectedStopIndex
            : Math.Max(route.StopCount, route.Stops.Count) - 1;
        if (legCount < 0 || route.Legs.Count < legCount)
          return null;
        foreach (var leg in route.Legs.Take(legCount))
        {
          if (!double.IsFinite(leg.Miles))
            return null;
          total += leg.Miles;
        }
        if (route == InspectedRoute)
          return total;
      }
      return null;
    }
  }
  private DispatchEta? InspectedEta
  {
    get
    {
      if (_inspectedDetailsRequest is not null && _inspectedDetails is null)
        return null;
      if (InspectedStop is not { Id: var stopId } || stopId == Guid.Empty)
        return null;
      var now = Clock.GetUtcNow().UtcDateTime;
      if (
        !InspectedEtaRefreshing
        && _routeState?.Eta is { Stops.Count: 0 } unavailable
      )
        return unavailable;
      var chain = DisplayRouteState?.Eta;
      var details = _inspectedExecutionLegId is null
        ? _inspectedDetails?.Eta
        : null;
      // Previously shown stop estimates retain their own grace; selecting
      // another stop cannot start it.
      var candidates = new[]
      {
        FleetRouteDisplayMemory.CanDisplay(chain, now, InspectedEtaRefreshing)
          ? chain
          : null,
        details?.ValidUntil.ToUniversalTime() > now ? details : null,
      }
        .Where(eta => eta is not null)
        .OrderByDescending(eta => eta!.CalculatedAt);
      foreach (var eta in candidates)
      {
        var stop = eta!.Stops.FirstOrDefault(stop =>
          stop.StopId == stopId && stop.DispatchId == _inspectedLoadId
        );
        if (stop is not null)
          return eta with { Stops = [stop], DutyStatus = null };
      }
      return null;
    }
  }
  private PlanStop? InspectedStop
  {
    get
    {
      var marker = InspectedRoute?.Stops.ElementAtOrDefault(
        _inspectedStopIndex
      );
      var source =
        marker is { Id: var id } && id != Guid.Empty
          ? _inspectedDetails?.Stops.FirstOrDefault(s =>
            s.Id == id && !s.DriverOnly
          )
          : _inspectedDetails
            ?.Stops.Where(s => !s.DriverOnly)
            .OrderBy(s => s.Sequence)
            .ElementAtOrDefault(_inspectedStopIndex);
      if (source is { } stop)
        return new(
          stop.Id,
          stop.Name,
          string.Join(
            ", ",
            new[]
            {
              stop.Address,
              stop.City,
              stop.Province,
              stop.ZipCode,
              stop.Country,
            }
              .Where(part => !string.IsNullOrWhiteSpace(part))
              .Distinct(StringComparer.OrdinalIgnoreCase)
          ),
          stop.Sequence,
          new((double)(stop.Latitude ?? 0), (double)(stop.Longitude ?? 0))
        )
        {
          Job =
            marker?.OperationRevision > stop.OperationRevision
              ? marker.Job
              : stop.Job,
          StateAfter =
            marker?.OperationRevision > stop.OperationRevision
              ? marker.StateAfter
              : stop.StateAfter,
          ScheduledDate = stop.ScheduledDate,
          ScheduledTime = stop.ScheduledTime,
          ScheduledDate2 = stop.ScheduledDate2,
          ScheduledTime2 = stop.ScheduledTime2,
          Commodity = stop.Commodity,
          Notes = stop.Notes,
        };
      return
        InspectedRoute?.Stops.ElementAtOrDefault(_inspectedStopIndex)
          is { } saved
        ? new(
          saved.Id,
          saved.Name,
          "",
          _inspectedStopIndex + 1,
          new(saved.Latitude, saved.Longitude)
        )
        {
          Job = saved.Job,
          StateAfter = saved.StateAfter,
        }
        : null;
    }
  }

  private void ToggleNextStopDetails() =>
    _nextStopDetailsOpen = !_nextStopDetailsOpen;

  private void ResetInspectedLoad()
  {
    if (_inspectorMode == MapInspectorMode.NextStop)
      _inspectorMode = HasTruckInspection
        ? MapInspectorMode.Truck
        : MapInspectorMode.Closed;
    ++_inspectedDetailsVersion;
    _inspectedDetailsRequest?.Cancel();
    _inspectedDetailsRequest = null;
    _inspectedLoadId = null;
    _inspectedExecutionLegId = null;
    _inspectedStopIndex = 0;
    _nextStopDetailsOpen = false;
    _inspectedDetails = null;
    _inspectedDetailsError = null;
  }

  [JSInvokable]
  public Task OnNextLoadSelected(
    string truck,
    string current,
    string? load,
    int stopIndex
  ) => SelectNextLoadAsync(truck, current, load, stopIndex, null, null, 0);

  [JSInvokable]
  public Task OnNextExecutionLegSelected(
    string truck,
    string current,
    string? load,
    int stopIndex,
    string? executionLeg,
    string? currentExecutionLeg,
    long currentAssignmentRevision
  ) =>
    SelectNextLoadAsync(
      truck,
      current,
      load,
      stopIndex,
      executionLeg,
      currentExecutionLeg,
      currentAssignmentRevision
    );

  private async Task SelectNextLoadAsync(
    string truck,
    string current,
    string? load,
    int stopIndex,
    string? executionLeg,
    string? currentExecutionLeg,
    long currentAssignmentRevision
  )
  {
    Guid? executionLegId = Guid.TryParse(executionLeg, out var leg)
      ? leg
      : null;
    Guid? currentExecutionLegId = Guid.TryParse(
      currentExecutionLeg,
      out var currentLeg
    )
      ? currentLeg
      : null;
    if (
      _disposed
      || MapOverlayOpen
      || !ShowNextLoads
      || !Guid.TryParse(truck, out var truckId)
      || truckId != _activeTruckId
      || !Guid.TryParse(current, out var currentId)
      || currentId != SelectedDispatchId
      || currentExecutionLegId != SelectedExecutionLegId
      || currentAssignmentRevision != SelectedAssignmentRevision
    )
      return;
    if (load is null)
    {
      ResetInspectedLoad();
      await InvokeAsync(StateHasChanged);
      return;
    }
    if (
      !Guid.TryParse(load, out var id)
      || id == currentId && executionLegId == currentExecutionLegId
      || !_nextLoadRoutes.Any(route =>
        route.Id == id && route.ExecutionLegId == executionLegId
      )
      || stopIndex < 0
    )
      return;
    var route = _nextLoadRoutes.First(route =>
      route.Id == id && route.ExecutionLegId == executionLegId
    );
    if (stopIndex >= Math.Max(1, Math.Max(route.StopCount, route.Stops.Count)))
      return;
    _inspectorMode = MapInspectorMode.NextStop;
    _showTruckInfo = true;
    if (
      _inspectedLoadId == id
      && _inspectedExecutionLegId == executionLegId
      && _inspectedDetailsRequest is not null
    )
    {
      if (_inspectedStopIndex != stopIndex)
        _nextStopDetailsOpen = false;
      _inspectedStopIndex = stopIndex;
      await InvokeAsync(StateHasChanged);
      return;
    }
    ResetInspectedLoad();
    _inspectorMode = MapInspectorMode.NextStop;
    _inspectedLoadId = id;
    _inspectedExecutionLegId = executionLegId;
    _inspectedStopIndex = stopIndex;
    _addressCopyMessage = null;
    var now = Clock.GetUtcNow();
    foreach (
      var key in _inspectedDetailsCache
        .Where(entry => entry.Value.Expires <= now)
        .Select(entry => entry.Key)
        .ToArray()
    )
      _inspectedDetailsCache.Remove(key);
    var identity = (truckId, currentId, id);
    if (
      _inspectedDetailsCache.TryGetValue(identity, out var cached)
      && route.Stops.All(marker =>
        marker.OperationRevision
        == (
          cached
            .Details.Stops.FirstOrDefault(s => s.Id == marker.Id)
            ?.OperationRevision ?? 0
        )
      )
    )
    {
      _inspectedDetails = cached.Details;
      await InvokeAsync(StateHasChanged);
      return;
    }
    var version = _inspectedDetailsVersion;
    using var request = CancellationTokenSource.CreateLinkedTokenSource(
      _lifetime.Token
    );
    _inspectedDetailsRequest = request;
    await InvokeAsync(StateHasChanged);
    try
    {
      var response = await Api.GetAsync<DispatchResponse>(
        $"api/dispatch/{id}",
        request.Token
      );
      if (
        _disposed
        || request.IsCancellationRequested
        || version != _inspectedDetailsVersion
        || truckId != _activeTruckId
        || currentId != SelectedDispatchId
        || currentExecutionLegId != SelectedExecutionLegId
        || currentAssignmentRevision != SelectedAssignmentRevision
        || id != _inspectedLoadId
        || executionLegId != _inspectedExecutionLegId
      )
        return;
      if (
        response.Success
        && response.Response is { } details
        && details.Id == id
      )
      {
        _inspectedDetails = details;
        if (_inspectedDetailsCache.Count >= 12)
          _inspectedDetailsCache.Remove(
            _inspectedDetailsCache.MinBy(entry => entry.Value.Expires).Key
          );
        _inspectedDetailsCache[identity] = (
          details,
          Clock.GetUtcNow().AddMinutes(5)
        );
      }
      else
        _inspectedDetailsError =
          "Load details are temporarily unavailable. Select the stop again to retry.";
    }
    catch (Exception ex) when (IsLoadError(ex))
    {
      if (
        !_disposed
        && !request.IsCancellationRequested
        && version == _inspectedDetailsVersion
      )
        _inspectedDetailsError =
          "Load details are temporarily unavailable. Select the stop again to retry.";
    }
    finally
    {
      if (ReferenceEquals(_inspectedDetailsRequest, request))
        _inspectedDetailsRequest = null;
      if (!_disposed && version == _inspectedDetailsVersion)
        await InvokeAsync(StateHasChanged);
    }
  }
}
