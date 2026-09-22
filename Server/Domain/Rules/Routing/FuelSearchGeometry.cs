using Domain.Models.Routing;

namespace Domain.Rules.Routing;

// Borrowed geometry is request-scoped and must not change during the search.
public sealed class FuelSearchGeometry
{
  public const int TargetBlockCount = RouteGeometryIndex.TargetBlockCount;
  private readonly RouteGeometryIndex index;

  public FuelSearchGeometry(TruckRoute route, CancellationToken ct = default)
  {
    index = new(route, capture: false, ct);
  }

  public double Miles => index.Miles;
  public int BlockCount => index.BlockCount;

  // A caller that only cares about the road within so many miles should say
  // so. Without it the index prunes blocks against the best distance it has
  // found, which for a point far from this road is hundreds of miles and
  // prunes nothing: the median station in a national catalogue walked the
  // whole route. Left at infinity the behaviour is exactly as before.
  public (
    double Along,
    double Away,
    RoutePoint Point,
    int SegmentsExamined
  ) Match(
    RoutePoint point,
    double minAlong = 0,
    CancellationToken ct = default,
    double maximumAwayMiles = double.PositiveInfinity
  ) => index.Match(point, minAlong, ct, maximumAwayMiles: maximumAwayMiles);

  public (
    double Along,
    double Away,
    RoutePoint Point,
    int SegmentsExamined
  ) MatchLeg(
    int leg,
    RoutePoint point,
    CancellationToken ct = default,
    double maximumAwayMiles = double.PositiveInfinity
  ) => index.MatchLeg(leg, point, ct, maximumAwayMiles);

  public RoutePoint At(double mile, CancellationToken ct = default) =>
    index.At(mile, ct);
}
