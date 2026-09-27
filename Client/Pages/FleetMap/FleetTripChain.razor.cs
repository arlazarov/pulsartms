using Client.Models.DTO.Dispatch;
using Client.Models.DTO.Fleet;
using Client.Shared.Dispatch;
using Microsoft.AspNetCore.Components;

namespace Client.Pages.FleetMap;

public partial class FleetTripChain
{
  [Parameter]
  public TruckLocationMapDto? Truck { get; set; }

  [Parameter]
  public IReadOnlyList<DispatchResponse> Loads { get; set; } = [];

  [Parameter]
  public bool Loading { get; set; }

  [Parameter]
  public bool Failed { get; set; }

  // The load the page's route and truck card are for.
  [Parameter]
  public Guid? CurrentId { get; set; }

  // The trip the panel under the truck shows, and its chosen stop.
  [Parameter]
  public DispatchResponse? SelectedTrip { get; set; }

  [Parameter]
  public Guid? FocusedStopId { get; set; }

  [Parameter]
  public bool NextLoadsShown { get; set; }

  [Parameter]
  public EventCallback<DispatchResponse> Selected { get; set; }

  [Parameter]
  public EventCallback<(
    DispatchResponse Load,
    Guid Stop
  )> StopSelected { get; set; }

  [Parameter]
  public EventCallback ShowNextLoads { get; set; }

  private readonly Dictionary<int, ElementReference> _cards = [];
  private int _position;

  private bool IsSelected(DispatchResponse load) =>
    SelectedTrip is { } trip
    && trip.Id == load.Id
    && trip.ExecutionLegId == load.ExecutionLegId;

  private async Task MoveAsync(int step)
  {
    var target = Math.Clamp(_position + step, 0, Loads.Count - 1);
    _position = target;
    if (_cards.TryGetValue(target, out var card))
      await card.FocusAsync();
  }

  protected override void OnParametersSet()
  {
    if (_position >= Loads.Count)
      _position = 0;
  }

  // The colour is the server's phase; an unplaced or stale load stays
  // neutral rather than borrowing a place.
  private static string PhaseClass(DispatchResponse load) =>
    load.Completed
      ? "is-completed"
      : load.WorkPhase switch
      {
        "current" => "is-current",
        "next" => "is-next",
        "upcoming" => "is-upcoming",
        _ => "is-unplaced",
      };

  private static string Lane(DispatchResponse load)
  {
    var stops = load.Stops.OrderBy(x => x.Sequence).ToList();
    return $"{DispatchBoardRow.Location(stops.FirstOrDefault(x => !x.DriverOnly))}"
      + $" → {DispatchBoardRow.Location(stops.LastOrDefault())}";
  }
}
