namespace Application.Features.Fleet.Models;

public class FleetLocationsResponse
{
  public IReadOnlyList<TruckLocation> Trucks { get; set; } = [];
  public IReadOnlyList<TruckLocationPoint> Points { get; set; } = [];
  // Changes with every published snapshot so unchanged reads can be answered without a body.
  public string? Revision { get; set; }
}
