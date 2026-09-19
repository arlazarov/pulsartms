namespace Client.Models.DTO.Fleet;

public class FleetLocationsMapDto
{
  public List<TruckLocationMapDto> Trucks { get; set; } = [];
  public List<TruckLocationPointMapDto> Points { get; set; } = [];
}
