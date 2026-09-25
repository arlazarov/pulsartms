using Client.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;

namespace Client.Pages.FleetMap;

// The map's place, kept in its own address so that a load opened from here,
// browser Back and a reload all come back to it: the chosen truck and load,
// the next load's stop being looked at, the camera and the truck search.
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
        _view,
        TruckSearch
      )
    );

  private string? OpenLoadHref =>
    SelectedDispatchId is { } id
      ? ReturnNavigation.Load(id, ReturnOrigin)
      : null;

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

  // The camera came to rest. Only the address follows; nothing is drawn.
  [JSInvokable]
  public Task OnMapViewChanged(double latitude, double longitude, double zoom)
  {
    var view = MapView.Parse(new MapView(latitude, longitude, zoom).ToString());
    if (view is null || view == _view)
      return Task.CompletedTask;
    _view = view;
    return ReflectSelectionAsync();
  }

  // Asked once, after the truck is focused; the map applies it when that
  // load's stops are drawn, through the same path as a click, so the
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
