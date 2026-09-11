namespace Client.Models.DTO.Planning;

public sealed class RecommendedFuelStation
{
  public Guid StationId { get; set; }
  public string Name { get; set; } = "";
  public string Address { get; set; } = "";
  public RoutePoint Point { get; set; } = new(0, 0);
  public double RouteMile { get; set; }
  public double? MilesAhead { get; set; }
  public decimal YourPrice { get; set; }
  public decimal? PriceAfterIfta { get; set; }
  public string Currency { get; set; } = "";
  public string Unit { get; set; } = "";
  public double? DetourMinutes { get; set; }
}
