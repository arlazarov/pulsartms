using Domain.Models.Routing;
using Domain.Rules.Routing;

namespace Server.Tests.Routing;

[Trait("Category", "Routing")]
[Trait("Kind", "Unit")]
public sealed class RemainingFuelRouteTests
{
  [Theory]
  [InlineData(0)]
  [InlineData(.005)]
  public void ArrivalAtPickupKeepsZeroLengthAnchorAndUnchangedDeliveryRoad(
    double latitudeOffset
  )
  {
    var pickup = new PlanStop(Guid.NewGuid(), "Pickup", "", 1, new(40, -79));
    var delivery = new PlanStop(
      Guid.NewGuid(),
      "Delivery",
      "",
      2,
      new(40, -78)
    );
    var deliveryLeg = new RouteLeg(100, 6000, [pickup.Point, delivery.Point]);
    var plan = new RoutePlan
    {
      FromCurrentPosition = true,
      Stops = [pickup, delivery],
      Route = new()
      {
        Miles = 150,
        Seconds = 9000,
        Legs = [new(50, 3000, [new(40, -80), pickup.Point]), deliveryLeg],
      },
    };

    var result = Assert.IsType<TruckRoute>(
      RemainingFuelRoute.TryRead(
        plan,
        plan.Stops,
        new(40 + latitudeOffset, -79),
        2
      )
    );

    Assert.Equal(2, result.Legs.Count);
    Assert.Equal(0, result.Legs[0].Miles);
    Assert.Equal(0, result.Legs[0].Seconds);
    Assert.Equal(new[] { pickup.Point, pickup.Point }, result.Legs[0].Points);
    Assert.Same(deliveryLeg, result.Legs[1]);
    Assert.Equal(100, result.Miles);
    Assert.Equal(6000, result.Seconds);
    Assert.True(SavedRouteGeometry.Complete(result, 2));
    Assert.Empty(plan.Tracking.PassedStopIds);
    Assert.Equal(150, plan.Route.Miles);
    plan.Route = result;
    Assert.NotNull(RemainingFuelRoute.TryRead(plan, plan.Stops, pickup.Point));
  }

  [Fact]
  public void ReusesOnlyRemainingRoadAndDoesNotMutateSavedRoute()
  {
    var stop = new PlanStop(Guid.NewGuid(), "Delivery", "", 1, new(40, -78));
    var plan = new RoutePlan
    {
      FromCurrentPosition = true,
      Stops = [stop],
      Route = new()
      {
        Miles = 100,
        Seconds = 6000,
        Legs = [new(100, 6000, [new(40, -80), new(40, -79), new(40, -78)])],
      },
    };
    var remaining = RemainingFuelRoute.TryRead(plan, [stop], new(40, -79));
    Assert.NotNull(remaining);
    Assert.Equal(50, remaining.Miles, 4);
    Assert.Equal(3000, remaining.Seconds, 4);
    Assert.Equal(100, plan.Route.Legs[0].Miles);
    Assert.Null(RemainingFuelRoute.TryRead(plan, [stop], new(41, -79)));
    plan.InputsChanged = true;
    Assert.Null(RemainingFuelRoute.TryRead(plan, [stop], new(40, -79)));
  }

  [Fact]
  public void WiderExplicitMatchReturnsOnlyTheSavedRoadAndKeepsTheLegacyDefaultStrict()
  {
    var stop = new PlanStop(Guid.NewGuid(), "Delivery", "", 1, new(40, -78));
    var plan = new RoutePlan
    {
      FromCurrentPosition = true,
      Stops = [stop],
      Route = new()
      {
        Miles = 100,
        Seconds = 6000,
        Legs = [new(100, 6000, [new(40, -80), new(40, -79), new(40, -78)])],
      },
    };
    var position = new RoutePoint(40.00324, -79);

    Assert.Null(RemainingFuelRoute.TryRead(plan, [stop], position));
    var remaining = Assert.IsType<TruckRoute>(
      RemainingFuelRoute.TryRead(plan, [stop], position, 2)
    );

    Assert.InRange(
      RouteGeometry.Distance(position, remaining.Legs[0].Points[0]),
      .223,
      .225
    );
    Assert.Equal(new RoutePoint(40, -79), remaining.Legs[0].Points[0]);
    Assert.Equal(50, remaining.Miles, 6);
    Assert.Equal(3000, remaining.Seconds, 6);
    Assert.DoesNotContain(position, remaining.Legs[0].Points);
    Assert.Equal(
      new[]
      {
        new RoutePoint(40, -80),
        new RoutePoint(40, -79),
        new RoutePoint(40, -78),
      },
      plan.Route.Legs[0].Points
    );
    Assert.Null(RemainingFuelRoute.TryRead(plan, [stop], new(40.04, -79), 2));
    Assert.NotNull(RemainingFuelRoute.TryRead(plan, [stop], new(40, -79), 0));
  }

  [Theory]
  [InlineData(-.01)]
  [InlineData(double.NaN)]
  [InlineData(double.PositiveInfinity)]
  [InlineData(double.NegativeInfinity)]
  public void MatchAllowanceMustBeFiniteAndNonnegative(double allowance)
  {
    Assert.Throws<ArgumentOutOfRangeException>(
      () => RemainingFuelRoute.TryRead(new(), [], new(40, -79), allowance)
    );
  }
}
