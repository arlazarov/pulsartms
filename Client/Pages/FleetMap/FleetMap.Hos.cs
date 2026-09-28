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
    await RefreshTruckDutyAsync(ct);
    await InvokeAsync(StateHasChanged);
    return _hosSnapshot.Values.Any(x => x.Hos is not null);
  }

  // The chosen truck's duty reading from its owner (GetTruckDutyStatus),
  // with or without a route: read for that one truck only, at the clocks'
  // cadence and when another truck is chosen; the server reads a driver's
  // history at most once a minute. A server without it leaves the panel
  // on the planning ETA's reading.
  private TruckDutyStatus? _truckDuty;
  private Guid? _truckDutyAsked;

  private async Task RefreshTruckDutyAsync(CancellationToken ct)
  {
    if (_activeTruckId is not { } truck)
      return;
    _truckDutyAsked = truck;
    var response = await Api.GetAsync<TruckDutyStatus>(
      $"api/fleet/trucks/{truck}/duty-status",
      ct
    );
    if (_disposed || ct.IsCancellationRequested || truck != _activeTruckId)
      return;
    _truckDuty = response.Success ? response.Response : null;
  }

  // A newly chosen truck is read at once rather than at the next tick.
  private async Task RefreshTruckDutyIfChosenAsync()
  {
    if (_activeTruckId is not { } truck || truck == _truckDutyAsked)
      return;
    await RefreshTruckDutyAsync(_lifetime.Token);
    if (!_disposed)
      await InvokeAsync(StateHasChanged);
  }

  private DriverHosClocks? SelectedHos(DriverHosClocks? fallback) =>
    _activeTruckId is { } id && _hosSnapshot.TryGetValue(id, out var snapshot)
      ? snapshot.Hos
      : fallback;

  private string HosDriverName(TruckLocationMapDto truck) =>
    _hosSnapshot.TryGetValue(truck.TruckId, out var snapshot)
      ? snapshot.DriverName
      : truck.DriverName;

  // Two sources disagree about the truck's trailer, said with both sources
  // named and neither chosen (TruckTrailerAuthority on the server). Either
  // telemetry reported a trailer - or none - and the truck's current work
  // names another, or two trucks resolved to the same trailer and neither
  // could keep it.
  public static (string Text, string Title)? TrailerDiscrepancy(
    TruckLocationMapDto truck
  )
  {
    var other = truck.TrailerConflictNumber?.Trim();
    if (string.IsNullOrEmpty(other))
      return null;
    var shown = string.IsNullOrWhiteSpace(truck.TrailerNumber)
      ? "none"
      : truck.TrailerNumber.Trim();
    return truck.TrailerSource switch
    {
      "telemetry" => (
        $"Telemetry: {shown} · Work: {other}",
        $"Trailer sources disagree: telemetry reports {(shown == "none" ? "no trailer" : shown)}; the truck's current work names {other}. Neither is chosen here."
      ),
      null or "" when shown == "none" => (
        $"{other}: also named for another truck",
        $"Trailer {other} is named for this truck and for another one; neither is confirmed, so it is not assigned to either."
      ),
      _ => (
        $"Trailer: {shown} · Also named: {other}",
        $"Trailer sources disagree: {TrailerTitle(truck).ToLowerInvariant()} is {shown}; another source names {other}. Neither is chosen here."
      ),
    };
  }

  // Where the truck's trailer came from, as the server resolved it.
  private static string TrailerTitle(TruckLocationMapDto truck) =>
    truck.TrailerSource switch
    {
      "telemetry" => "Trailer, as telemetry reports it",
      "execution" => "Trailer of the accepted work",
      "load" => "Trailer of the current load",
      _ => "Trailer",
    };
}
