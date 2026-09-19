using Client.Models.DTO.Fleet;
using Client.Models.DTO.Planning;
using Client.Services;

namespace Client.Pages.FleetMap;

public partial class FleetMap
{
  private Dictionary<Guid, TruckHosSnapshot> _hosSnapshot = [];
  private Task? _hosPollingTask;

  private async Task<bool> RefreshHosAsync(CancellationToken ct)
  {
    var response = await Api.GetAsync<Dictionary<Guid, TruckHosSnapshot>>(
      "api/fleet/hos",
      ct
    );
    if (
      _disposed
      || ct.IsCancellationRequested
      || !response.Success
      || response.Response is null
    )
      return false;
    _hosSnapshot = response.Response;
    if (_activeTruckId is { } id)
      _hos = _hosSnapshot.GetValueOrDefault(id)?.Hos;
    await InvokeAsync(StateHasChanged);
    return _hosSnapshot.Values.Any(x => x.Hos is not null);
  }

  private DriverHosClocks? SelectedHos(DriverHosClocks? fallback) =>
    _activeTruckId is { } id && _hosSnapshot.TryGetValue(id, out var snapshot)
      ? snapshot.Hos
      : fallback;

  private string HosDriverName(TruckLocationMapDto truck) =>
    _hosSnapshot.TryGetValue(truck.TruckId, out var snapshot)
      ? snapshot.DriverName
      : truck.DriverName;
}
