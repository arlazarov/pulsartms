namespace Client.Models.DTO.Planning;

public sealed class TruckRoute
{
  public DateTime CalculatedAt { get; set; } = DateTime.UtcNow;
  public double Miles { get; set; }
  public double Seconds { get; set; }
  public List<RouteLeg> Legs { get; set; } = [];
  public List<RoutePoint> Points { get; set; } = [];
  public List<string> Warnings { get; set; } = [];
}
