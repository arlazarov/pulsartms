namespace Application.Features.Routing.Models;

public sealed record RouteSegmentMeaning(
  string CargoState,
  string Purpose,
  string GeometrySource = "EstimatedRoad"
);
