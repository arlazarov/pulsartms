using Client.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;

namespace Client.Pages.FleetMap;

// The map's place, kept in its own address so that a load opened from here,
// browser Back and a reload all come back to it: the chosen truck and load,
// the next load's stop being looked at and the truck search. The camera is
// kept out of the address, in this tab's storage for the signed-in user,
// and comes back only to the same truck (or to none), so a link to another
// truck still frames that truck. A `view` in an older link is used once and
// then dropped from the address by the usual replace.
public partial class FleetMap
{
  [Inject]
  private ReturnPlaces Places { get; set; } = default!;

  [SupplyParameterFromQuery(Name = "q")]
  public string? Search { get; set; }

  [SupplyParameterFromQuery(Name = "view")]
  public string? View { get; set; }

  [SupplyParameterFromQuery(Name = "nextLoadId")]
  public Guid? NextLoadId { get; set; }

  [SupplyParameterFromQuery(Name = "nextStop")]
  public int? NextStop { get; set; }

  [SupplyParameterFromQuery(Name = "nextLeg")]
  public Guid? NextLeg { get; set; }

  private MapView? _view;
  private string? _reflectedAddress;

  // The next load's stop named by the address is opened once the truck's
  // next loads are drawn; until then, or until the reader chooses something
  // else, the address keeps naming it.
  private bool _nextRestorePending;
  private bool _nextRestoreRequested;

  private void ReadReturnPlace()
  {
    TruckSearch = Search?.Trim() ?? "";
    _view = MapView.Parse(View);
    _nextRestorePending = NextLoadId.HasValue;
  }

  private object? InitialView =>
    _view is { } view
      ? new
      {
        latitude = view.Latitude,
        longitude = view.Longitude,
        zoom = view.Zoom,
      }
      : null;

  // Before a selection settles, the truck and load the map was opened with
  // stand, so a reload while it loads loses nothing.
  private bool SelectionSettled =>
    _activeTruckId is not null || _selectionDismissed;
  private bool InspectingNextStop =>
    _inspectorMode == MapInspectorMode.NextStop && _inspectedLoadId is not null;

  private string ReturnOrigin =>
    ReturnNavigation.FleetMap(
      new MapPlace(
        SelectionSettled ? _activeTruckId : TruckId,
        SelectionSettled ? SelectedDispatchId : DispatchId,
        InspectingNextStop ? _inspectedLoadId
          : _nextRestorePending ? NextLoadId
          : null,
        InspectingNextStop ? _inspectedStopIndex : NextStop ?? 0,
        InspectingNextStop ? _inspectedExecutionLegId
          : _nextRestorePending ? NextLeg
          : null,
        TruckSearch
      )
    );

  // Written into the map's own history entry, never pushed: browser Back
  // leaves the map rather than stepping through its selections.
  private async Task ReflectSelectionAsync()
  {
    if (_disposed)
      return;
    var address = ReturnOrigin;
    if (address == _reflectedAddress)
      return;
    _reflectedAddress = address;
    await Places.ReflectAsync(address);
  }

  // The camera came to rest. Only the tab's record follows; nothing is
  // drawn and the address is not touched.
  [JSInvokable]
  public Task OnMapViewChanged(double latitude, double longitude, double zoom)
  {
    var view = MapView.Parse(new MapView(latitude, longitude, zoom).ToString());
    if (view is null || view == _view)
      return Task.CompletedTask;
    _view = view;
    return SaveCameraAsync(view);
  }

  private string? CameraKey =>
    _preferencesKey is null ? null : _preferencesKey + ".camera";

  private async Task SaveCameraAsync(MapView view)
  {
    if (_disposed || CameraKey is not { } key)
      return;
    var truck = SelectionSettled ? _activeTruckId : TruckId;
    try
    {
      await JS.InvokeVoidAsync(
        "sessionStorage.setItem",
        _lifetime.Token,
        key,
        $"{truck?.ToString() ?? ""}|{view}"
      );
    }
    catch (JSException) { }
    catch (OperationCanceledException) when (_disposed) { }
  }

  private async Task RestoreCameraAsync()
  {
    if (_view is not null || _disposed || CameraKey is not { } key)
      return;
    try
    {
      var saved = await JS.InvokeAsync<string?>(
        "sessionStorage.getItem",
        _lifetime.Token,
        key
      );
      if (
        saved?.Split('|') is [var truck, var place]
        && truck == (TruckId?.ToString() ?? "")
      )
        _view = MapView.Parse(place);
    }
    catch (JSException) { }
    catch (OperationCanceledException) when (_disposed) { }
  }

  // Asked once, as soon as the truck is focused; the map applies it when
  // that load's stops are drawn, through the same path as a click, so the
  // truck, load and assignment checks still decide.
  private async Task RestoreNextStopAsync()
  {
    if (
      !_nextRestorePending
      || _nextRestoreRequested
      || NextLoadId is not { } load
      || _map is null
      || _disposed
    )
      return;
    _nextRestoreRequested = true;
    await _map.InvokeVoidAsync(
      "selectNextStop",
      load.ToString(),
      Math.Max(0, NextStop ?? 0),
      NextLeg?.ToString()
    );
  }
}
