using Client.Models.DTO.Planning;
using Client.Shared;
using Client.Shared.Fuel;
using Client.Shared.Fuel.FuelPlanEditor;
using Microsoft.JSInterop;

namespace Client.Pages.FleetMap;

public partial class FleetMap
{
  private readonly record struct FuelEditorIdentity(
    Guid Truck,
    Guid Dispatch,
    Guid? Leg,
    long Revision
  );

  private bool _fuelEditorOpen;
  private FuelEditorIdentity? _fuelEditorIdentity;
  private FuelEditorStation? _fuelEditorStation;
  private long _fuelStationSequence;
  private bool CanEditFuel =>
    !_disposed
    && _activeTruckId is { } truck
    && _routeState?.Plan is { Tracking.AllStopsPassed: false } plan
    && plan.TruckId == truck
    && plan.DispatchId != Guid.Empty
    && plan.DispatchId == SelectedDispatchId
    && plan.ExecutionLegId == SelectedExecutionLegId
    && plan.AssignmentRevision == SelectedAssignmentRevision;

  private bool OwnsFuelEditor(FuelEditorIdentity identity) =>
    _fuelEditorIdentity == identity
    && identity.Truck == _activeTruckId
    && identity.Dispatch == SelectedDispatchId
    && identity.Leg == SelectedExecutionLegId
    && identity.Revision == SelectedAssignmentRevision;

  private async Task OpenFuelEditorAsync()
  {
    if (
      !CanEditFuel
      || _recalculatingFuel
      || _routeEditorDispatch.HasValue
      || _cameraOpen
    )
      return;
    var identity = new FuelEditorIdentity(
      _activeTruckId!.Value,
      SelectedDispatchId!.Value,
      SelectedExecutionLegId,
      SelectedAssignmentRevision
    );
    if (!_fuelEditorOpen && _map is not null)
      await _map.InvokeVoidAsync("closeStationPopup", true);
    if (
      !CanEditFuel
      || identity.Truck != _activeTruckId
      || identity.Dispatch != SelectedDispatchId
      || identity.Leg != SelectedExecutionLegId
      || identity.Revision != SelectedAssignmentRevision
    )
      return;
    _fuelEditorIdentity = identity;
    _fuelEditorOpen = true;
    ResetInspectedLoad();
    await PublishInspectorSuspensionAsync();
    if (_map is { } map)
      await map.InvokeVoidAsync(
        "setFuelEditorTruck",
        _lifetime.Token,
        identity.Truck.ToString()
      );
  }

  [JSInvokable]
  public async Task OnFuelStationEdit(
    string truckId,
    string dispatchId,
    string stationId,
    string name,
    string? beforeStopId,
    bool addNew
  )
  {
    if (
      !CanEditFuel
      || _recalculatingFuel
      || _routeEditorDispatch.HasValue
      || _cameraOpen
      || !Guid.TryParse(truckId, out var truck)
      || truck != _activeTruckId
      || !Guid.TryParse(dispatchId, out var dispatch)
      || dispatch != SelectedDispatchId
      || !Guid.TryParse(stationId, out var station)
      || station == Guid.Empty
    )
      return;
    var before =
      Guid.TryParse(beforeStopId, out var stop) && stop != Guid.Empty
        ? (Guid?)stop
        : null;
    _fuelEditorStation = new(
      station,
      string.IsNullOrWhiteSpace(name) ? "Fuel station"
        : name.Length > 200 ? name[..200]
        : name,
      before,
      addNew,
      ++_fuelStationSequence
    );
    await OpenFuelEditorAsync();
    await InvokeAsync(StateHasChanged);
  }

  private async Task CloseFuelEditorAsync(FuelEditorIdentity identity)
  {
    if (
      _disposed
      || !OwnsFuelEditor(identity)
      || _activeTruckId != identity.Truck
      || SelectedDispatchId != identity.Dispatch
    )
      return;
    ResetFuelEditor();
    await ClearFuelEditorFocusAsync(identity);
  }

  private Task OnFuelEditorBusyChangedAsync(
    FuelEditorIdentity identity,
    bool busy
  ) =>
    OwnsFuelEditor(identity)
    && _activeTruckId == identity.Truck
    && SelectedDispatchId == identity.Dispatch
      ? OnFuelBusyChanged(busy)
      : Task.CompletedTask;

  private Task OnFuelEditorFailedAsync(FuelEditorIdentity identity) =>
    OwnsFuelEditor(identity)
    && _activeTruckId == identity.Truck
    && SelectedDispatchId == identity.Dispatch
      ? OnFuelFailed()
      : Task.CompletedTask;

  private async Task OnFuelEditorStationSelectedAsync(
    FuelEditorIdentity identity,
    FuelPlanStop? station
  )
  {
    if (
      !CanEditFuel
      || !_fuelEditorOpen
      || !OwnsFuelEditor(identity)
      || identity.Truck != _activeTruckId
      || identity.Dispatch != SelectedDispatchId
      || _map is not { } map
    )
      return;
    if (station is null)
    {
      await ClearFuelEditorFocusAsync();
      return;
    }
    if (station.StationId == Guid.Empty)
      return;
    await map.InvokeVoidAsync(
      "focusFuelStation",
      _lifetime.Token,
      new
      {
        TruckId = identity.Truck,
        DispatchId = identity.Dispatch,
        station.StationId,
        station.Point,
        station.Number,
        station.Name,
        station.Address,
        station.BeforeStopId,
        station.YourPrice,
        station.EconomicPrice,
        station.Currency,
        station.Unit,
      }
    );
  }

  private async Task ClearFuelEditorFocusAsync(
    FuelEditorIdentity? returnToRoute = null
  )
  {
    if (!_disposed && _map is { } map)
    {
      await map.InvokeVoidAsync(
        "setFuelEditorTruck",
        _lifetime.Token,
        _fuelEditorOpen ? _fuelEditorIdentity?.Truck.ToString() : null
      );
      await map.InvokeVoidAsync(
        "clearFuelStationFocus",
        _lifetime.Token,
        !_fuelEditorOpen,
        returnToRoute is { } identity
          ? new { TruckId = identity.Truck, DispatchId = identity.Dispatch }
          : null
      );
    }
  }

  private void ResetFuelEditor()
  {
    _fuelEditorOpen = false;
    _fuelEditorIdentity = null;
    _fuelEditorStation = null;
  }

  private async Task OnFuelPlanSavedAsync(FuelPlanEditPreview preview)
  {
    if (
      _disposed
      || _fuelEditorIdentity is not { } identity
      || !OwnsFuelEditor(identity)
      || preview.Plan.TruckId != identity.Truck
      || preview.Plan.ExecutionLegId != identity.Leg
      || identity.Leg.HasValue
        && preview.Plan.AssignmentRevision != identity.Revision
      || _routeState?.Plan is not { } plan
      || plan.TruckId != identity.Truck
      || plan.DispatchId != identity.Dispatch
    )
      return;
    ResetFuelEditor();
    await ClearFuelEditorFocusAsync();
    if (
      _disposed
      || SelectedDispatchId != identity.Dispatch
      || _activeTruckId != identity.Truck
      || !ReferenceEquals(_routeState?.Plan, plan)
    )
      return;
    _routeRequest?.Cancel();
    _routeRequest = null;
    _routeLoading = false;
    plan.FuelPlan = preview.Plan;
    SetRouteState(_routeState with { });
    PlanningCache.StoreRecalculated(
      new(
        identity.Truck,
        identity.Dispatch,
        _loadDetails?.LoadNumber,
        _routeState,
        null
      )
      {
        Hos = _hos,
        ExecutionLegId = identity.Leg,
        AssignmentRevision = identity.Revision,
      }
    );
    await SendMapRouteAsync(false);
    await LoadRouteAsync(false, force: true);
  }

  private async Task OnFuelPlanResetAsync(AutomaticPlanningResult result)
  {
    if (
      _disposed
      || _fuelEditorIdentity is not { } identity
      || !OwnsFuelEditor(identity)
      || result.TruckId != identity.Truck
      || result.DispatchId != identity.Dispatch
      || result.ExecutionLegId != identity.Leg
      || result.AssignmentRevision != identity.Revision
      || SelectedDispatchId != identity.Dispatch
    )
      return;
    _recalculatingFuel = false;
    ResetFuelEditor();
    await ClearFuelEditorFocusAsync();
    if (
      _disposed
      || SelectedDispatchId != identity.Dispatch
      || _activeTruckId != identity.Truck
    )
      return;
    _routeRequest?.Cancel();
    _routeRequest = null;
    _routeLoading = false;
    PlanningCache.StoreRecalculated(result);
    await OnFuelRecalculated(result);
  }
}
