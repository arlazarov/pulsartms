namespace Domain.Models.Fleet;

public class FleetLocationsResponse
{
  public IReadOnlyList<TruckLocation> Trucks { get; set; } = [];
  public IReadOnlyList<TruckLocation> Points { get; set; } = [];
}
