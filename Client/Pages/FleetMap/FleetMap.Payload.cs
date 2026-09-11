using Client.Services;
using Microsoft.JSInterop;

namespace Client.Pages.FleetMap;

public partial class FleetMap
{
  private readonly MapRoutePublisher _routePublisher = new();

  private async Task SendMapLoadReferenceAsync()
  {
    if (_disposed || _map is not { } map) return;
    var details = _loadDetails;
    await map.InvokeVoidAsync("setLoadReference", _lifetime.Token,
      details is not null && _routeState?.Plan?.DispatchId == details.Id ? new
      {
        DispatchId = details.Id, details.LoadNumber, details.OrderNumber,
        LoadLabel = Client.Shared.Dispatch.LoadNumberDisplay.Format(details.LoadNumber, DisplaySettings?.LoadNumberPrefix)
      } : null);
  }

  private void SetRouteState(Client.Models.DTO.Planning.RoutePlanningState? state,
    Guid? responseTruckId = null, Guid? responseDispatchId = null)
  {
    var now = Clock.GetUtcNow().UtcDateTime;
    if (state is { Plan: null, Eta.RouteUpdatePending: true } && _routeState?.Plan is { } saved
      && saved.TruckId == responseTruckId && saved.DispatchId == responseDispatchId
      && saved.Tracking.NextStopId is { } nextId && !saved.Tracking.AllStopsPassed
      && (state.Eta.Stops.FirstOrDefault(stop => stop.DispatchId == saved.DispatchId) is not { } incoming
        || incoming.StopId == nextId)
      && !(_loadDetails?.Id == saved.DispatchId && _loadDetails.Stops.Any(stop => stop.Id == nextId
        && (stop.DepartedAt ?? stop.DeliveredAt ?? stop.PickedUpAt).HasValue)))
    {
      var candidate = state with { Plan = saved };
      // Only the display receives saved geometry; the provider response and cache remain unchanged.
      if (_routeDisplay.IsRetaining(candidate, now)) state = candidate;
    }
    var sameStops = _routeDisplay.Matches(state);
    if (!sameStops || !_routeDisplay.MatchesGeometry(state))
    {
      _displayRemainingMiles = null;
      _displayProgressMiles = null;
      _retainedProgressUntil = null;
      if (!sameStops) _arrivalMemory.Update(null, null, null);
    }
    _routeState = state;
    if (_fuelEditorIdentity is { } editing && (state?.Plan?.TruckId != editing.Truck || state.Plan.DispatchId != editing.Dispatch))
      ResetFuelEditor();
    _routeDisplay.Update(state, now, _displayRemainingMiles, _displayProgressMiles);
    if (RetainingRouteDisplay && DisplayRouteState?.Eta is { } retained)
      _retainedProgressUntil = retained.ValidUntil.ToUniversalTime().AddMinutes(15);
  }

  private async Task SendMapStopEtasAsync()
  {
    if (_disposed || _map is not { } map) return;
    await map.InvokeVoidAsync("setStopEtas", _routeState?.Plan is { } plan ? new
    {
      PlanId = plan.Id, PlanVersion = plan.Version, plan.TruckId,
      CurrentDispatchId = plan.DispatchId, Eta = DisplayRouteState?.Eta, Refreshing = _etaRefreshPending
    } : null);
  }

  private async Task SendMapRouteAsync(bool fit)
  {
    var state = _routeState;
    var display = DisplayRouteState;
    var map = _map;
    if (_disposed || map is null) return;
    try
    {
      await _routePublisher.PublishAsync(map, display, fit,
        () => !_disposed && ReferenceEquals(map, _map) && ReferenceEquals(state, _routeState), _lifetime.Token);
      if (!_disposed && ReferenceEquals(map, _map) && ReferenceEquals(state, _routeState))
      {
        await SendMapLoadReferenceAsync();
        await SendMapStopEtasAsync();
      }
    }
    catch (OperationCanceledException) when (_lifetime.IsCancellationRequested) { }
  }
}
