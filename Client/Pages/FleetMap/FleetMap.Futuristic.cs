using Client.Models.DTO.Planning;
using Client.Shared.Appearance.AppearanceProvider;
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;

namespace Client.Pages.FleetMap;

// The Futuristic interface adds a truck list and a trip chain around the
// same map. Both only route into the page's existing selection paths, so
// selection, Follow and the inspector behave as in the current interface.
public partial class FleetMap
{
  [CascadingParameter]
  private AppearanceProvider? Appearance { get; set; }

  private bool Futuristic => Appearance?.Futuristic == true;

  // As choosing a truck in search: focus it on the map, then select it.
  private async Task ChooseListTruckAsync(Guid truckId)
  {
    if (_map is null || _disposed)
      return;
    if (truckId == _activeTruckId)
    {
      await SelectRouteAsync(truckId, _activeDispatchId);
      return;
    }
    if (
      await _map.InvokeAsync<bool>("focusTruck", truckId.ToString(), (int?)null)
    )
      await SelectRouteAsync(truckId, null);
  }

  // Back to the truck and its current load; the camera does not move, so an
  // active Follow continues.
  private Task ChooseCurrentLoadAsync() =>
    _activeTruckId is { } truck
      ? SelectRouteAsync(truck, _activeDispatchId)
      : Task.CompletedTask;

  private async Task ChooseNextLoadAsync(NextLoadRoute route)
  {
    if (_map is null || _disposed)
      return;
    await _map.InvokeVoidAsync(
      "selectNextStop",
      route.Id.ToString(),
      0,
      route.ExecutionLegId?.ToString()
    );
  }

  private async Task ShowNextLoadsFromChainAsync()
  {
    if (ShowNextLoads)
      return;
    ShowNextLoads = true;
    await OnNextLoadsChanged();
  }
}
