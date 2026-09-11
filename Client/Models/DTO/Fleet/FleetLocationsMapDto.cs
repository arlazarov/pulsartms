namespace Client.Models.DTO.Fleet;

public class FleetLocationsMapDto
{
  public List<TruckLocationMapDto> Trucks { get; set; } = [];
  public List<TruckLocationMapDto> Points { get; set; } = [];
}
