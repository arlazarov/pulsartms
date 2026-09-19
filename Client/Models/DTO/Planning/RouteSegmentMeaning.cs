namespace Client.Models.DTO.Planning;

public sealed record RouteSegmentMeaning(
  string CargoState,
  string Purpose,
  string GeometrySource = "EstimatedRoad"
);
