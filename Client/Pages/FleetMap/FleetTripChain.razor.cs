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
  public DispatchResponse? Current { get; set; }

  [Parameter]
  public bool CurrentLoading { get; set; }

  [Parameter]
  public IReadOnlyList<NextLoadRoute> Next { get; set; } = [];

  [Parameter]
  public bool NextLoadsShown { get; set; }

  [Parameter]
  public Guid? InspectedLoadId { get; set; }

  [Parameter]
  public Guid? InspectedLegId { get; set; }

  [Parameter]
  public EventCallback CurrentSelected { get; set; }

  [Parameter]
  public EventCallback<NextLoadRoute> NextSelected { get; set; }

  [Parameter]
  public EventCallback ShowNextLoads { get; set; }

  private static string Lane(DispatchResponse load)
  {
    var stops = load.Stops.OrderBy(x => x.Sequence).ToList();
    var from = stops.FirstOrDefault(x => !x.DriverOnly);
    var to = stops.LastOrDefault();
    return $"{Place(from)} → {Place(to)}";
  }

  private static string Lane(NextLoadRoute route)
  {
    var first = route.Stops.FirstOrDefault()?.Name;
    var last = route.Stops.LastOrDefault()?.Name;
    var count = Math.Max(route.StopCount, route.Stops.Count);
    return string.IsNullOrWhiteSpace(first)
      ? $"{count} stops"
      : $"{first.Trim()} → {last?.Trim()}";
  }

  private static string Place(DispatchStopResponse? stop)
  {
    if (stop is null)
      return "Pending";
    var parts = new[] { stop.City, stop.Province }.Where(x =>
      !string.IsNullOrWhiteSpace(x)
    );
    var text = string.Join(", ", parts);
    return text.Length > 0 ? text : stop.Name;
  }
}
