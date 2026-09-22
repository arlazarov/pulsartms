using Domain.Models.Routing;
using Domain.Rules.Routing;

namespace Server.Tests.Routing;

[Trait("Category", "Routing")]
[Trait("Kind", "Unit")]
public sealed class RouteMovementTests
{
  private static readonly DateTime Start = new(
    2026,
    9,
    21,
    0,
    0,
    0,
    DateTimeKind.Utc
  );

  private static RouteGeometry Geometry() =>
    new(new() { Legs = [new(100, 6000, [new(40, -80), new(40, -79)])] });

  [Fact]
  public void ThousandsOfOnRoadObservationsKeepOnlyOneMeasuredRange()
  {
    var plan = new RoutePlan();
    var geometry = Geometry();
    for (var i = 0; i < 1000; i++)
      RouteMovementRecorder.Observe(
        plan,
        geometry,
        1,
        new(Start.AddSeconds(i * 10), new(40, -80 + i * .001))
      );
    Assert.Empty(plan.CompletedMovement);
    var open = Assert.IsType<RouteMovement>(plan.Tracking.Movement);
    Assert.Equal("RouteMatchedEstimate", open.Kind);
    Assert.Equal(2, open.Observations.Count);
    Assert.Equal(0, open.FromMiles);
    Assert.Equal(99.9, open.ToMiles!.Value, 5);
  }

  [Fact]
  public void GapsAndReverseTravelCannotBecomeContinuousForwardHistory()
  {
    var plan = new RoutePlan();
    var geometry = Geometry();
    RouteMovementRecorder.Observe(
      plan,
      geometry,
      1,
      new(Start, new(40, -79.5))
    );
    RouteMovementRecorder.Observe(
      plan,
      geometry,
      1,
      new(Start.AddSeconds(10), new(40, -79.5005))
    );
    Assert.Equal("ObservedDeviation", plan.Tracking.Movement!.Kind);
    RouteMovementRecorder.Observe(
      plan,
      geometry,
      1,
      new(Start.AddMinutes(30), new(40, -79.7))
    );
    Assert.Contains(plan.CompletedMovement, x => x.Kind == "Gap");
    Assert.All(
      plan.CompletedMovement.Where(x => x.Kind == "Gap"),
      x => Assert.Null(x.ToMiles)
    );
  }

  [Fact]
  public void DeviationChunksAreBoundedAndPreserveEndpoints()
  {
    var plan = new RoutePlan();
    var geometry = Geometry();
    for (var i = 0; i < 200; i++)
      RouteMovementRecorder.Observe(
        plan,
        geometry,
        1,
        new(Start.AddSeconds(i), new(41, -80 + i * .00001))
      );
    Assert.Equal(3, plan.CompletedMovement.Count);
    Assert.All(
      plan.CompletedMovement,
      x =>
      {
        Assert.Equal("ObservedDeviation", x.Kind);
        Assert.Equal(2, x.Observations.Count);
      }
    );
    Assert.True(plan.Tracking.Movement!.Observations.Count <= 64);
    Assert.Equal(Start, plan.CompletedMovement[0].Observations[0].At);
    Assert.Equal(
      Start.AddSeconds(199),
      plan.Tracking.Movement.Observations[^1].At
    );
  }

  [Fact]
  public void DuplicateAndOldObservationsDoNotChangeCheckpoint()
  {
    var plan = new RoutePlan();
    var geometry = Geometry();
    var point = new RouteObservation(Start, new(40, -79.5));
    RouteMovementRecorder.Observe(plan, geometry, 1, point);
    RouteMovementRecorder.Observe(plan, geometry, 1, point);
    RouteMovementRecorder.Observe(
      plan,
      geometry,
      1,
      point with
      {
        At = Start.AddSeconds(-1),
      }
    );
    Assert.Single(plan.Tracking.Movement!.Observations);
    Assert.Empty(plan.CompletedMovement);
  }

  [Fact]
  public void OverlappingRoadCannotBeRecordedAsACertainForwardRange()
  {
    var plan = new RoutePlan();
    var a = new RoutePoint(40, -80);
    var b = new RoutePoint(40, -79);
    var geometry = new RouteGeometry(
      new() { Legs = [new(300, 18000, [a, b, a, b])] }
    );
    RouteMovementRecorder.Observe(
      plan,
      geometry,
      1,
      new(Start, new(40, -79.5))
    );
    Assert.Equal("ObservedDeviation", plan.Tracking.Movement!.Kind);
    Assert.Null(plan.Tracking.Movement.ToMiles);
  }
}
