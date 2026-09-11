using Client.Shared.Fuel.FuelPlanEditor;
using Client.Shared.Fuel;
using Client.Models.DTO.Planning;
using Client.Shared;
using Microsoft.JSInterop;

namespace Client.Pages.FleetMap;

public partial class FleetMap
{
    private bool _fuelEditorOpen;
    private (Guid Truck, Guid Dispatch)? _fuelEditorIdentity;
    private FuelEditorStation? _fuelEditorStation;
    private long _fuelStationSequence;
    private bool CanEditFuel => !_disposed && _activeTruckId is { } truck
        && _routeState?.Plan is { Tracking.AllStopsPassed: false } plan
        && plan.TruckId == truck && plan.DispatchId != Guid.Empty && plan.DispatchId == SelectedDispatchId;

    private async Task OpenFuelEditorAsync()
    {
        if (!CanEditFuel || _recalculatingFuel) return;
        var identity = (Truck: _activeTruckId!.Value, Dispatch: SelectedDispatchId!.Value);
        if (!_fuelEditorOpen && _map is not null) await _map.InvokeVoidAsync("closeStationPopup", true);
        if (!CanEditFuel || identity.Truck != _activeTruckId || identity.Dispatch != SelectedDispatchId) return;
        _fuelEditorIdentity = identity;
        _fuelEditorOpen = true;
        _mobileDetailsOpen = false;
        ResetInspectedLoad();
        if (_map is { } map) await map.InvokeVoidAsync("setFuelEditorTruck", _lifetime.Token, identity.Truck.ToString());
    }

    [JSInvokable]
    public async Task OnFuelStationEdit(string truckId, string dispatchId, string stationId,
        string name, string? beforeStopId, bool addNew)
    {
        if (!CanEditFuel || _recalculatingFuel || !Guid.TryParse(truckId, out var truck) || truck != _activeTruckId
            || !Guid.TryParse(dispatchId, out var dispatch) || dispatch != SelectedDispatchId
            || !Guid.TryParse(stationId, out var station) || station == Guid.Empty) return;
        var before = Guid.TryParse(beforeStopId, out var stop) && stop != Guid.Empty ? (Guid?)stop : null;
        _fuelEditorStation = new(station, string.IsNullOrWhiteSpace(name) ? "Fuel station" : name.Length > 200 ? name[..200] : name,
            before, addNew, ++_fuelStationSequence);
        await OpenFuelEditorAsync();
        await InvokeAsync(StateHasChanged);
    }

    private async Task CloseFuelEditorAsync()
    {
        ResetFuelEditor();
        await ClearFuelEditorFocusAsync();
    }

    private async Task OnFuelEditorStationSelectedAsync((Guid Truck, Guid Dispatch) identity, FuelPlanStop? station)
    {
        if (!CanEditFuel || !_fuelEditorOpen || _fuelEditorIdentity != identity
            || identity.Truck != _activeTruckId || identity.Dispatch != SelectedDispatchId || _map is not { } map) return;
        if (station is null)
        {
            await ClearFuelEditorFocusAsync();
            return;
        }
        if (station.StationId == Guid.Empty) return;
        await map.InvokeVoidAsync("focusFuelStation", _lifetime.Token, new
        {
            TruckId = identity.Truck, DispatchId = identity.Dispatch, station.StationId,
            station.Point, station.Number, station.Name, station.Address, station.BeforeStopId,
            station.YourPrice, station.EconomicPrice, station.Currency, station.Unit
        });
    }

    private async Task ClearFuelEditorFocusAsync()
    {
        if (!_disposed && _map is { } map)
        {
            await map.InvokeVoidAsync("setFuelEditorTruck", _lifetime.Token,
                _fuelEditorOpen ? _fuelEditorIdentity?.Truck.ToString() : null);
            await map.InvokeVoidAsync("clearFuelStationFocus", _lifetime.Token, !_fuelEditorOpen);
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
        if (_disposed || _fuelEditorIdentity is not { } identity || preview.Plan.TruckId != identity.Truck
            || _routeState?.Plan is not { } plan || plan.TruckId != identity.Truck || plan.DispatchId != identity.Dispatch) return;
        ResetFuelEditor();
        await ClearFuelEditorFocusAsync();
        if (_disposed || SelectedDispatchId != identity.Dispatch || _activeTruckId != identity.Truck
            || !ReferenceEquals(_routeState?.Plan, plan)) return;
        _routeRequest?.Cancel();
        _routeRequest = null;
        _routeLoading = false;
        plan.FuelPlan = preview.Plan;
        SetRouteState(_routeState with { });
        PlanningCache.StoreRecalculated(new(identity.Truck, identity.Dispatch, _loadDetails?.LoadNumber,
            _routeState, null) { Hos = _hos });
        await SendMapRouteAsync(false);
        await LoadRouteAsync(false, force: true);
    }

    private async Task OnFuelPlanResetAsync(AutomaticPlanningResult result)
    {
        if (_disposed || _fuelEditorIdentity is not { } identity || result.TruckId != identity.Truck
            || result.DispatchId != identity.Dispatch || SelectedDispatchId != identity.Dispatch) return;
        ResetFuelEditor();
        await ClearFuelEditorFocusAsync();
        if (_disposed || SelectedDispatchId != identity.Dispatch || _activeTruckId != identity.Truck) return;
        _routeRequest?.Cancel();
        _routeRequest = null;
        _routeLoading = false;
        PlanningCache.StoreRecalculated(result);
        await OnFuelRecalculated(result);
    }
}
