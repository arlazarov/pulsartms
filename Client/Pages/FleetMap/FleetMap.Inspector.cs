using Client.Models.DTO.Fleet;
using Client.Models.DTO.Planning;
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
    FuelPlan,
    FuelStations,
  }

  // The fuel view a station card was opened from: its Back returns there
  // rather than to the truck. Null when the card was picked on the map.
  private MapInspectorMode? _fuelReturn;

  private MapInspectorMode _inspectorMode;
  private long _inspectorVersion;
  private bool _cameraOpen;
  private bool _inspectorSuspended;
  private TruckCamera? _truckCamera;
  private bool MapOverlayOpen =>
    _fuelEditorOpen
    || _sendPlanOpen
    || _routeEditorDispatch.HasValue
    || _cameraOpen;
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

  // The speed is known only from a report, and only while the route does
  // not call the truck's GPS stale: otherwise the card says nothing about
  // it rather than a confident 0 mph "normal".
  private decimal? KnownSpeed(TruckLocationMapDto truck) =>
    truck.UpdatedAt == default || _routeState?.Progress?.LocationStale == true
      ? null
      : truck.Speed;

  // The duty status the forecast read with the hours: its start, the rest
  // built up so far and the ruleset they are read under. The summary drops
  // the durations whenever the live clocks name another status.
  private DriverDutyStatus? HeadDutyStatus =>
    DisplayRouteState?.Eta?.DutyStatus;

  // What the fuel plan says about reaching its first stop with the reserve
  // in the tank - a different fact from a low reading, which is the
  // tank's percentage alone.
  // The plan the Fuel view shows: the selected route's own, while it is
  // usable.
  private FuelPlan? CurrentFuelPlan =>
    _routeState?.Plan is { FuelPlan: { } fuel } && Usable(fuel) ? fuel : null;

  // Kept until a replacement is published: its route inputs changed or the
  // owner asks for an update.
  private bool FuelPlanStale =>
    _routeState?.Plan is { FuelPlan: { } fuel } plan
    && (plan.InputsChanged || fuel.Stale);

  // A stale plan's reserve figures are not current: no warning from them.
  private string? FuelReserveWarning =>
    _routeState?.Plan is { InputsChanged: false, FuelPlan: { } fuel }
    && Usable(fuel)
    && !fuel.Stale
    && fuel.Stops.FirstOrDefault(stop => stop.Warning.Length > 0) is { } stop
      ? $"Fuel stop {stop.Number}: {stop.Warning}"
      : null;

  private string InspectorTitle =>
    _inspectorMode switch
    {
      MapInspectorMode.Stop => "Route stop",
      MapInspectorMode.Fuel => _fuelReturn == MapInspectorMode.FuelPlan
        ? "Planned fuel stop"
        : "Fuel station",
      MapInspectorMode.FuelPlan => "Fuel plan",
      MapInspectorMode.FuelStations => "Fuel stations",
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
    // The map knows the truck card, not the fuel views the page draws in
    // its place: the truck it reports is the one they belong to.
    if (
      mode == MapInspectorMode.Truck
      && _inspectorMode
        is MapInspectorMode.FuelPlan
          or MapInspectorMode.FuelStations
    )
      return Task.CompletedTask;
    if (mode == MapInspectorMode.Fuel)
      _fuelReturn =
        _inspectorMode
          is MapInspectorMode.FuelPlan
            or MapInspectorMode.FuelStations
          ? _inspectorMode
        : _inspectorMode == MapInspectorMode.Fuel ? _fuelReturn
        : null;
    else
      _fuelReturn = null;
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
    if (
      _inspectorMode
      is not (
        MapInspectorMode.Truck
        or MapInspectorMode.FuelPlan
        or MapInspectorMode.FuelStations
      )
    )
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
    if (_inspectorMode == MapInspectorMode.Fuel && _fuelReturn is { } fuelView)
    {
      await ShowFuelViewAsync(fuelView);
      return;
    }
    if (_fuelEditorFromPlan)
    {
      _fuelEditorFromPlan = false;
      await ShowFuelViewAsync(MapInspectorMode.FuelPlan);
      return;
    }
    _fuelReturn = null;
    ResetInspectedLoad();
    _inspectorMode = MapInspectorMode.Truck;
    _showTruckInfo = true;
    if (_map is not null && !_disposed)
      await _map.InvokeVoidAsync(
        "setInspectorMode",
        "truck",
        InspectorTruckId?.ToString()
      );
  }

  // Fuel opens the plan in the card's place; the plan's own actions open
  // the editor, the send window, a station or the station list.
  private Task OpenFuelPlanAsync() =>
    ShowFuelViewAsync(MapInspectorMode.FuelPlan);

  private Task BackToFuelPlanAsync() =>
    ShowFuelViewAsync(MapInspectorMode.FuelPlan);

  private async Task OpenFuelStationsAsync()
  {
    await ShowFuelViewAsync(MapInspectorMode.FuelStations);
    if (_stations?.LoadedDate != SelectedDate)
      await OnDateChanged();
  }

  private async Task ShowFuelViewAsync(MapInspectorMode view)
  {
    if (!HasTruckInspection || MapOverlayOpen)
      return;
    ResetInspectedLoad();
    _fuelReturn = null;
    _inspectorMode = view;
    _showTruckInfo = true;
    if (_map is not null && !_disposed)
      await _map.InvokeVoidAsync(
        "setInspectorMode",
        "truck",
        InspectorTruckId?.ToString()
      );
    _inspectorMode = view;
  }

  // A station opens in its own card, as a click on its marker opens it;
  // Back returns to the view it was opened from.
  private Task OpenPlannedStationAsync(FuelPlanStop stop) =>
    OpenStationCardAsync(stop.StationId, MapInspectorMode.FuelPlan);

  private Task OpenListedStationAsync(Guid stationId) =>
    OpenStationCardAsync(stationId, MapInspectorMode.FuelStations);

  private async Task OpenStationCardAsync(Guid stationId, MapInspectorMode from)
  {
    if (_map is null || _disposed || stationId == Guid.Empty)
      return;
    var opened = await _map.InvokeAsync<bool>(
      "openStation",
      stationId.ToString()
    );
    if (opened && _inspectorMode == MapInspectorMode.Fuel)
      _fuelReturn = from;
  }

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
