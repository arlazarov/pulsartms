namespace Domain.Models.Routing;

public sealed record RouteChunkRange(string Key, int Start, int Count);

public sealed record RouteChunkManifest(
  int Format,
  List<List<RouteChunkRange>> Route,
  List<List<RouteChunkRange>>? Reference,
  List<RouteChunkMeasure> Measures,
  List<RouteChunkMeasure>? ReferenceMeasures
);

public sealed record RouteChunkMeasure(double Miles, double Seconds);
