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

  // AMF1414 after the border release: tracking kept the saved plan's leg
  // through Ontario between two US stops and bought only the connector.
  // A kept remainder that leaves its country is bought again whole, once,
  // through the provider's check; a domestic one is kept (above).
  [Fact]
  public async Task ARemainderThatLeavesItsCountryIsBoughtAgainWhole()
  {
    await using var f = await RouteChoiceFixture.CreateAsync();
    var a = new RoutePoint(40, -80);
    var buffalo = new RoutePoint(42.8864, -78.8784);
    var detroit = new RoutePoint(42.3314, -83.0458);
    var stops = new List<PlanStop>
    {
      new(Guid.NewGuid(), "", "", 1, a),
      new(Guid.NewGuid(), "", "", 2, buffalo),
      new(Guid.NewGuid(), "", "", 3, detroit),
    };
    var throughOntario = new RouteLeg(
      250,
      16000,
      [buffalo, new(42.9849, -81.2453), detroit]
    );
    var previous = new RoutePlan
    {
      Stops = stops,
      Route = RouteViaGeometry.Join(
        [new(200, 12000, [a, buffalo]), throughOntario],
        [],
        DateTime.UtcNow
      ),
    };
    var position = new RoutePoint(41, -79.5);

    var result = await f.Planning.BaseRoutes.RecalculateAsync(
      RouteWorkProjection.Capture(f.Load),
      new(),
      position,
      stops.Skip(1).ToList(),
      previous,
      default
    );

    Assert.Equal(new[] { position, buffalo, detroit }, f.Routing.LastPoints);
    Assert.Equal(1, f.Routing.Calls);
    Assert.DoesNotContain(throughOntario, result.Legs);
  }
}
