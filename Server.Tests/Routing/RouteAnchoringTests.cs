using Application.Features.Routing.Algorithms;
using Application.Features.Routing.Exceptions;
using Application.Features.Routing.Models;
using Application.Features.Routing.Services.FuelPlanning;

namespace Server.Tests.Routing;

[Trait("Category", "Routing")]
[Trait("Kind", "Unit")]
public sealed class RouteAnchoringTests
{
  [Theory]
  [InlineData(.08044, true)]
  [InlineData(.49, true)]
  [InlineData(.51, false)]
  [InlineData(2.61, false)]
  public void FacilityRoadSnappingKeepsTheExistingHalfMileTolerance(
    double offsetMiles,
    bool expected
  )
  {
    var from = new RoutePoint(40, -80);
    var to = new RoutePoint(41, -79);
    var snapped = to with { Latitude = to.Latitude + offsetMiles / 69 };
    Assert.Equal(
      expected,
      RouteAnchoring.Matches(Route(from, snapped), [from, to])
    );
    Assert.Equal(
      expected,
      RouteAnchoring.Matches(Route(snapped, from), [to, from])
    );
  }

  [Theory]
  [InlineData(.04, true)]
  [InlineData(.08, false)]
  public void AdjacentLegsMustConnectEvenWhenBothEndpointsAreWithinTheFacility(
    double gapMiles,
    bool expected
  )
  {
    var from = new RoutePoint(40, -80);
    var stop = new RoutePoint(41, -79);
    var to = new RoutePoint(42, -78);
    var nextStart = stop with { Latitude = stop.Latitude + gapMiles / 69 };
    var first = Route(from, stop);
    var next = Route(nextStart, to);
    var joined = new TruckRoute
    {
      Miles = 200,
      Seconds = 12000,
      Legs = [first.Legs[0], next.Legs[0]],
    };
    Assert.Equal(expected, RouteAnchoring.Matches(joined, [from, stop, to]));
    Assert.Equal(expected, RouteAnchoring.Continuous(first, next));
    if (!expected)
      Assert.Throws<RoutePlanningException>(
        () => FuelHorizonRoad.Join(first, next)
      );
    else
    {
      var result = FuelHorizonRoad.Join(first, next);
      Assert.Equal(200, result.Miles);
      Assert.Equal(12000, result.Seconds);
      Assert.Same(first.Legs[0], result.Legs[0]);
      Assert.Same(next.Legs[0], result.Legs[1]);
    }
  }

  [Fact]
  public void MissingOrInvalidEndpointsNeverPassAnchorValidation()
  {
    var from = new RoutePoint(40, -80);
    var to = new RoutePoint(41, -79);
    Assert.False(RouteAnchoring.Matches(null, [from, to]));
    Assert.False(RouteAnchoring.Matches(new(), [from, to]));
    Assert.False(
      RouteAnchoring.Matches(new() { Legs = [new(100, 6000, [])] }, [from, to])
    );
    Assert.False(
      RouteAnchoring.Matches(Route(from, new(double.NaN, -79)), [from, to])
    );
    Assert.False(RouteAnchoring.Continuous(Route(from, to), new()));
  }

  private static TruckRoute Route(RoutePoint from, RoutePoint to) =>
    new()
    {
      Miles = 100,
      Seconds = 6000,
      Points = [from, to],
      Legs = [new(100, 6000, [from, to])],
    };
}
