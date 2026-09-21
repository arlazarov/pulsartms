namespace Domain.Models.Routing;

public sealed record RouteSegmentMeaning(
  string CargoState,
  string Purpose,
  string GeometrySource = "EstimatedRoad"
);
