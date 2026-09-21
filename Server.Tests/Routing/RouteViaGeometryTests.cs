using Application.Features.Dispatch.Models;
using Application.Features.Routing.Algorithms;
using Application.Features.Routing.Exceptions;
using Application.Features.Routing.Models;
using Application.Features.Routing.Services.FuelPlanning;
using Application.Features.Routing.Services.Routes;
using Application.Features.Synchronization.Options;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using Server.Tests.Support;

namespace Server.Tests.Routing;

[Trait("Category", "Routing")]
[Trait("Kind", "Unit")]
public sealed class RouteViaGeometryTests
{
  [Fact]
  public void NewChoiceDoesNotReuseThePreviousRoadsFailureCooldown()
  {
    var id = Guid.NewGuid();
    var state = new RoutePlanningState(new(), null, null, null, null, true);
    using var cache = new MemoryCache(new MemoryCacheOptions());
    var queue = new PlanningRefreshQueue(
      null!,
      null!,
      cache,
      Options.Create(new SynchronizationOptions()),
      TimeProvider.System
    );
    var key = PlanningRefreshQueue.ErrorKey(id, state, "one");
    cache.Set(key, "Previous road failure");
    Assert.NotEqual(
      "Previous road failure",
      queue.Message(id, state, "changed")
    );
    Assert.Equal("Previous road failure", queue.Message(id, state, "one"));
    Assert.NotEqual(
      "Previous road failure",
      queue.Message(id, state with { RouteChoiceRevision = 1 }, "one")
    );
    cache.Set(
      PlanningRefreshQueue.ErrorKey(
        id,
        state with
        {
          RouteChoiceRevision = 1,
        },
        "one"
      ),
      "First choice failure"
    );
    Assert.Equal(
      "First choice failure",
      queue.Message(id, state with { RouteChoiceRevision = 1 }, "one")
    );
    Assert.NotEqual(
      "First choice failure",
      queue.Message(id, state with { RouteChoiceRevision = 2 }, "one")
    );
  }

  [Fact]
  public void ChangingAnyFutureRoadInvalidatesTheFuelHorizon()
  {
    var current = new DispatchResponse { Id = Guid.NewGuid() };
    var next = new DispatchResponse { Id = Guid.NewGuid() };
    var original = FuelWorkSignature.Signature([current, next]);
    var currentSignature = FuelWorkSignature.LoadSignature(current);
    var nextSignature = FuelWorkSignature.LoadSignature(next);
    next.RouteChoiceRevision = 1;
    Assert.NotEqual(original, FuelWorkSignature.Signature([current, next]));
    Assert.NotEqual(nextSignature, FuelWorkSignature.LoadSignature(next));
    Assert.Equal(currentSignature, FuelWorkSignature.LoadSignature(current));
  }

  private static List<PlanStop> Stops() =>
    [
      new(Guid.NewGuid(), "Pickup", "", 1, new(40, -80)),
      new(Guid.NewGuid(), "Delivery", "", 2, new(43, -80)),
    ];

  [Fact]
  public void ViaPointsChangeGeometryWithoutAddingLoadStopsOrServiceTime()
  {
    var stops = Stops();
    List<RouteViaPoint> via =
    [
      new(Guid.NewGuid(), stops[1].Id, "Via one", new(41, -80)),
      new(Guid.NewGuid(), stops[1].Id, "Via two", new(42, -80)),
    ];
    var expanded = RouteViaGeometry.Expand(stops, via);
    var raw = RouteViaGeometry.Join(
      expanded
        .Points.Zip(
          expanded.Points.Skip(1),
          (a, b) => new RouteLeg(50, 3600, [a, b])
        )
        .ToList(),
      [],
      DateTime.UtcNow
    );
    var collapsed = RouteViaGeometry.Collapse(raw, expanded.StopIndexes);
    Assert.Equal(new[] { 0, 3 }, expanded.StopIndexes);
    Assert.Single(collapsed.Legs);
    Assert.Equal(expanded.Points, collapsed.Legs[0].Points);
    Assert.Equal(150, collapsed.Miles);
    Assert.Equal(10800, collapsed.Seconds);
    Assert.Equal(2, stops.Count);
  }

  [Theory]
  [InlineData("first-stop")]
  [InlineData("unknown-stop")]
  [InlineData("invalid-point")]
  [InlineData("duplicate")]
  [InlineData("too-many")]
  public void InvalidViaPointsAreRejected(string scenario)
  {
    var stops = Stops();
    var point = new RouteViaPoint(
      Guid.NewGuid(),
      stops[1].Id,
      "Via",
      new(41, -80)
    );
    List<RouteViaPoint> via = scenario switch
    {
      "first-stop" => [point with { BeforeStopId = stops[0].Id }],
      "unknown-stop" => [point with { BeforeStopId = Guid.NewGuid() }],
      "invalid-point" => [point with { Point = new(95, -80) }],
      "duplicate" => [point, point],
      _ => Enumerable
        .Range(0, 21)
        .Select(_ => point with { Id = Guid.NewGuid() })
        .ToList(),
    };
    Assert.Throws<RoutePlanningException>(
      () => RouteViaGeometry.Expand(stops, via)
    );
  }

  [Fact]
  public void RejoiningInsideSegmentKeepsTheSelectedRoadAhead()
  {
    RouteLeg leg = new(200, 7200, [new(40, -80), new(42, -80), new(42, -78)]);
    var remaining = RouteViaGeometry.Remaining(leg, new(41, -80));
    Assert.Equal(new RoutePoint(41, -80), remaining.Points[0]);
    Assert.Equal(leg.Points.Skip(1), remaining.Points.Skip(1));
    Assert.InRange(remaining.Miles, 100, 200);
    Assert.Equal(remaining.Miles / 200 * 7200, remaining.Seconds, 6);
  }
}
