namespace Domain.Models.Routing;

public sealed record RouteObservation(DateTime At, RoutePoint Point);

public sealed class RouteMovement
{
  public Guid Id { get; set; } = Guid.NewGuid();
  public Guid TruckId { get; set; }
  public Guid? NextStopId { get; set; }
  public long GeometryRevision { get; set; }
  public string Kind { get; set; } = "ObservedDeviation";
  public double? FromMiles { get; set; }
  public double? ToMiles { get; set; }
  public List<RouteObservation> Observations { get; set; } = [];
}
