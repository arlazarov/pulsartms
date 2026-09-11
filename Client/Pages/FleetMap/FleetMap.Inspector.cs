using Microsoft.AspNetCore.Components.Web;
using Microsoft.JSInterop;

namespace Client.Pages.FleetMap;

public partial class FleetMap
{
    private enum MapInspectorMode { Closed, Truck, Stop, Fuel, NextStop }
    private MapInspectorMode _inspectorMode;
    private long _inspectorVersion;
    private bool HasTruckInspection => _activeTruckId.HasValue || _activeDispatchId.HasValue;
    private Guid? InspectorTruckId => _activeTruckId ?? (_routeState?.Plan is { } plan
        && plan.DispatchId == SelectedDispatchId ? plan.TruckId : null);
    private bool InspectorVisible => _showTruckInfo && _inspectorMode != MapInspectorMode.Closed
        && (_inspectorMode != MapInspectorMode.Truck || HasTruckInspection);
    private string InspectorTitle => _inspectorMode switch
    {
        MapInspectorMode.Stop => "Route stop",
        MapInspectorMode.Fuel => "Fuel station",
        MapInspectorMode.NextStop => "Next load stop",
        _ => SelectedTruck is { } truck ? $"Truck {truck.UnitNumber}" : "Truck and route"
    };

    [JSInvokable]
    public Task OnMapInspectorChanged(string kind, string? truckId, long version)
    {
        if (_disposed || version <= _inspectorVersion) return Task.CompletedTask;
        _inspectorVersion = version;
        if (truckId is not null && (!Guid.TryParse(truckId, out var truck) || truck != InspectorTruckId)
            || truckId is null && InspectorTruckId.HasValue) return Task.CompletedTask;
        var mode = kind switch
        {
            "truck" => MapInspectorMode.Truck,
            "stop" => MapInspectorMode.Stop,
            "fuel" => MapInspectorMode.Fuel,
            "next-stop" => MapInspectorMode.NextStop,
            "closed" => MapInspectorMode.Closed,
            _ => (MapInspectorMode?)null
        };
        if (mode is null) return Task.CompletedTask;
        if (mode != MapInspectorMode.NextStop) ResetInspectedLoad();
        _inspectorMode = mode.Value;
        _showTruckInfo = mode != MapInspectorMode.Closed;
        if (mode != MapInspectorMode.Truck) _mobileDetailsOpen = true;
        return RefreshInspectorAsync();
    }

    private async Task RefreshInspectorAsync()
    {
        await InvokeAsync(StateHasChanged);
        if (_inspectorMode == MapInspectorMode.Fuel && _stations?.LoadedDate != SelectedDate)
            await OnDateChanged();
    }

    private async Task BackToTruckAsync()
    {
        if (!HasTruckInspection) { await CloseInspectorAsync(); return; }
        ResetInspectedLoad();
        _inspectorMode = MapInspectorMode.Truck;
        _showTruckInfo = true;
        _mobileDetailsOpen = false;
        if (_map is not null && !_disposed)
            await _map.InvokeVoidAsync("setInspectorMode", "truck", InspectorTruckId?.ToString());
    }

    private async Task CloseInspectorAsync()
    {
        ResetInspectedLoad();
        _inspectorMode = MapInspectorMode.Closed;
        _showTruckInfo = false;
        _mobileDetailsOpen = false;
        if (_map is not null && !_disposed) await _map.InvokeVoidAsync("clearMapInspection");
    }

    private Task OnInspectorKeyDownAsync(KeyboardEventArgs args) => args.Key == "Escape"
        ? _inspectorMode == MapInspectorMode.Truck ? CloseInspectorAsync() : BackToTruckAsync()
        : Task.CompletedTask;
}
