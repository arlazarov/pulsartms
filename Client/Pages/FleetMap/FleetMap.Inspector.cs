using Client.Models.DTO.Fleet;
using Client.Shared.Trucks.TruckCamera;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.JSInterop;

namespace Client.Pages.FleetMap;

public partial class FleetMap
{
  private enum MapInspectorMode
  {
    Closed,
    Truck,
    Stop,
    Fuel,
    NextStop,
  }

  private MapInspectorMode _inspectorMode;
  private long _inspectorVersion;
  private bool _cameraOpen;
  private bool _inspectorSuspended;
  private bool _mobileTruckDetailsOpen;
  private TruckCamera? _truckCamera;
  private bool MapOverlayOpen =>
    _fuelEditorOpen || _routeEditorDispatch.HasValue || _cameraOpen;
  private bool HasTruckInspection =>
    _activeTruckId.HasValue || _activeDispatchId.HasValue;
  private Guid? InspectorTruckId =>
    _activeTruckId
    ?? (
      _routeState?.Plan is { } plan && plan.DispatchId == SelectedDispatchId
        ? plan.TruckId
        : null
    );
  private bool InspectorVisible =>
    !MapOverlayOpen
    && _showTruckInfo
    && _inspectorMode != MapInspectorMode.Closed
    && (_inspectorMode != MapInspectorMode.Truck || HasTruckInspection);
  private string TruckLocationLabel =>
    SelectedDate == DateOnly.FromDateTime(DateTime.Today)
      ? "Current location"
      : "Recorded location";

  private static DateTimeOffset? TruckLocationTimestamp(
    TruckLocationMapDto truck
  ) =>
    truck.UpdatedAt == default
      ? null
      : new DateTimeOffset(
        DateTime.SpecifyKind(truck.UpdatedAt, DateTimeKind.Utc)
      );

  private string InspectorTitle =>
    _inspectorMode switch
    {
      MapInspectorMode.Stop => "Route stop",
      MapInspectorMode.Fuel => "Fuel station",
      MapInspectorMode.NextStop => "Next load stop",
      _ => SelectedTruck is { } truck
        ? $"Truck {truck.UnitNumber}"
        : "Truck and route",
    };

  [JSInvokable]
  public Task OnMapInspectorChanged(string kind, string? truckId, long version)
  {
    if (_disposed || version <= _inspectorVersion)
      return Task.CompletedTask;
    _inspectorVersion = version;
    if (MapOverlayOpen)
      return Task.CompletedTask;
    if (
      truckId is not null
        && (!Guid.TryParse(truckId, out var truck) || truck != InspectorTruckId)
      || truckId is null && InspectorTruckId.HasValue
    )
      return Task.CompletedTask;
    var mode = kind switch
    {
      "truck" => MapInspectorMode.Truck,
      "stop" => MapInspectorMode.Stop,
      "fuel" => MapInspectorMode.Fuel,
      "next-stop" => MapInspectorMode.NextStop,
      "closed" => MapInspectorMode.Closed,
      _ => (MapInspectorMode?)null,
    };
    if (mode is null)
      return Task.CompletedTask;
    if (mode != MapInspectorMode.NextStop)
      ResetInspectedLoad();
    _inspectorMode = mode.Value;
    _showTruckInfo = mode != MapInspectorMode.Closed;
    return RefreshInspectorAsync();
  }

  private async Task RefreshInspectorAsync()
  {
    await InvokeAsync(StateHasChanged);
    if (
      _inspectorMode == MapInspectorMode.Fuel
      && _stations?.LoadedDate != SelectedDate
    )
      await OnDateChanged();
  }

  [JSInvokable]
  public async Task OnMapBackgroundClicked(string? truckId, long version)
  {
    if (
      _disposed
      || !InspectorVisible
      || version != _inspectorVersion
      || truckId != InspectorTruckId?.ToString()
    )
      return;
    if (_inspectorMode != MapInspectorMode.Truck)
      await BackToTruckAsync();
    await InvokeAsync(StateHasChanged);
  }

  private Task OpenCameraAsync() =>
    !MapOverlayOpen && _truckCamera is not null
      ? _truckCamera.OpenAsync()
      : Task.CompletedTask;

  private Task OnCameraOpenChangedAsync(bool open)
  {
    _cameraOpen = open;
    return PublishInspectorSuspensionAsync();
  }

  private async Task PublishInspectorSuspensionAsync()
  {
    if (_disposed || _map is null || _inspectorSuspended == MapOverlayOpen)
      return;
    _inspectorSuspended = MapOverlayOpen;
    await _map.InvokeVoidAsync("setInspectionSuspended", _inspectorSuspended);
    if (!_disposed && !MapOverlayOpen)
    {
      await BackToTruckAsync();
      await InvokeAsync(StateHasChanged);
    }
  }

  private async Task BackToTruckAsync()
  {
    if (!HasTruckInspection)
    {
      await CloseInspectorAsync();
      return;
    }
    ResetInspectedLoad();
    _mobileTruckDetailsOpen = false;
    _inspectorMode = MapInspectorMode.Truck;
    _showTruckInfo = true;
    if (_map is not null && !_disposed)
      await _map.InvokeVoidAsync(
        "setInspectorMode",
        "truck",
        InspectorTruckId?.ToString()
      );
  }

  private void ToggleMobileTruckDetails() =>
    _mobileTruckDetailsOpen = !_mobileTruckDetailsOpen;

  // Closing puts the map back the way it was before anything was picked.
  // From a stop it used to leave the truck selected behind the card it had
  // just dismissed, so the map stayed on one truck with nothing to say why.
  // A station nobody reached through a truck is its own case: there is no
  // selection to drop, and dropping one would empty the search with it.
  private async Task CloseInspectorAsync()
  {
    ResetInspectedLoad();
    if (_map is not null && !_disposed)
      await _map.InvokeVoidAsync("clearMapInspection");
    if (HasTruckInspection)
    {
      await DeselectTruckAsync();
      return;
    }
    _inspectorMode = MapInspectorMode.Closed;
    _showTruckInfo = false;
  }

  private Task OnInspectorKeyDownAsync(KeyboardEventArgs args) =>
    args.Key == "Escape"
      ? _inspectorMode == MapInspectorMode.Truck
        ? CloseInspectorAsync()
        : BackToTruckAsync()
      : Task.CompletedTask;
}
