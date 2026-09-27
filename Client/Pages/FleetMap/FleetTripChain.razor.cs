using Client.Models.DTO.Dispatch;
using Client.Models.DTO.Fleet;
using Client.Models.DTO.Planning;
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

  [Parameter]
  public IReadOnlyList<NextLoadRoute> Next { get; set; } = [];

  [Parameter]
  public bool NextLoadsShown { get; set; }

  [Parameter]
  public Guid? InspectedLoadId { get; set; }

  [Parameter]
  public Guid? InspectedLegId { get; set; }

  [Parameter]
  public EventCallback<DispatchResponse> Selected { get; set; }

  [Parameter]
  public EventCallback ShowNextLoads { get; set; }

  private bool Drawn(DispatchResponse load) =>
    NextLoadsShown
    && Next.Any(route =>
      route.Id == load.Id && route.ExecutionLegId == load.ExecutionLegId
    );

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
    var from = stops.FirstOrDefault(x => !x.DriverOnly);
    var to = stops.LastOrDefault();
    return $"{Place(from)} → {Place(to)}";
  }

  private static string Place(DispatchStopResponse? stop)
  {
    if (stop is null)
      return "Pending";
    var text = string.Join(
      ", ",
      new[] { stop.City, stop.Province }.Where(x =>
        !string.IsNullOrWhiteSpace(x)
      )
    );
    return text.Length > 0 ? text : stop.Name;
  }
}
