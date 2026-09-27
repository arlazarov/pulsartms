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

  // The trip the panel under the truck shows.
  [Parameter]
  public DispatchResponse? SelectedTrip { get; set; }

  [Parameter]
  public EventCallback<DispatchResponse> Selected { get; set; }

  // Where a card's load opens, returning to this map.
  [Parameter]
  public Func<DispatchResponse, string>? LoadHref { get; set; }

  private bool IsSelected(DispatchResponse load) =>
    SelectedTrip is { } trip
    && trip.Id == load.Id
    && trip.ExecutionLegId == load.ExecutionLegId;

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
