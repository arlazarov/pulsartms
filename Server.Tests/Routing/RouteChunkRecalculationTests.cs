using Domain.Models.Routing;
using Domain.Rules.Routing;
using Server.Tests.Support;

namespace Server.Tests.Routing;

[Trait("Category", "Routing")]
[Trait("Kind", "Integration")]
public sealed class RouteChunkRecalculationTests
{
  [Fact]
  public async Task RecalculationCallsOnlyTheNextStopAndRetainsOnwardMeasures()
  {
    await using var f = await RouteChoiceFixture.CreateAsync();
    var a = new RoutePoint(40, -80);
    var b = new RoutePoint(41, -80);
    var c = new RoutePoint(42, -80);
    var stops = new List<PlanStop>
    {
      new(Guid.NewGuid(), "", "", 1, a),
      new(Guid.NewGuid(), "", "", 2, b),
      new(Guid.NewGuid(), "", "", 3, c),
    };
    var tail = new RouteLeg(83, 1234, [b, new(41.5, -79), c]);
    var previous = new RoutePlan
    {
      Stops = stops,
      Route = RouteViaGeometry.Join(
        [new(100, 6000, [a, b]), tail],
        [],
        DateTime.UtcNow
      ),
    };
    var position = new RoutePoint(40.5, -79.5);
    var result = await f.Planning.BaseRoutes.RecalculateAsync(
      RouteWorkProjection.Capture(f.Load),
      new(),
      position,
      stops.Skip(1).ToList(),
      previous,
      default
    );
    Assert.Equal(new[] { position, b }, f.Routing.LastPoints);
    Assert.Equal(1, f.Routing.Calls);
    Assert.Same(tail, result.Legs[1]);
    Assert.Equal(183, result.Miles);
    Assert.Equal(4834, result.Seconds);
  }
}
