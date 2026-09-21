using Application.Features.Routing.Services.FuelPlanning;
using Domain.Models.Routing;
using Domain.Rules;
using Domain.Rules.Routing;
using Xunit.Abstractions;

namespace Server.Tests.Fuel;

[Trait("Category", "Fuel")]
[Trait("Kind", "Allocation")]
public sealed class FuelHorizonMemoryTests(ITestOutputHelper output)
{
  [Fact]
  public void JoiningDenseLoadsReusesLegsWithoutCopyingTheFlatPath()
  {
    var first = Route(25_001);
    var next = Route(25_001);
    GC.KeepAlive(FuelHorizonRoad.Join(first, next));
    var before = GC.GetAllocatedBytesForCurrentThread();
    var joined = FuelHorizonRoad.Join(first, next);
    var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
    Assert.True(
      allocated < 4096,
      $"Route join copied detailed coordinates: {allocated:N0} bytes."
    );
    Assert.Empty(joined.Points);
    Assert.Same(first.Legs[0], joined.Legs[0]);
    Assert.Same(next.Legs[0], joined.Legs[1]);
    Assert.Equal(200, joined.Miles);
    Assert.Equal(12000, joined.Seconds);
    Assert.Equal(25_001, first.Points.Count);
    output.WriteLine(
      $"Joining 50,002 leg points: {allocated:N0} allocated bytes; original legs shared, no flat coordinate copy."
    );
  }

  [Fact]
  public void AggregateGeometryLimitRejectsTheJoinBeforeAllocatingAnotherPath()
  {
    var first = Route(100_000);
    var next = Route(100_001);
    var failure = Assert.Throws<RoutePlanningException>(
      () => FuelHorizonRoad.Join(first, next)
    );
    Assert.Contains("geometry limit", failure.Message);
    Assert.Single(first.Legs);
    Assert.Single(next.Legs);
    Assert.Equal(100_000, first.Points.Count);
    Assert.Equal(100_001, next.Points.Count);
    Assert.Equal(
      200_000,
      FuelHorizonRoad.Join(first, first).Legs.Sum(leg => leg.Points.Count)
    );
  }

  private static TruckRoute Route(int count)
  {
    var point = new RoutePoint(40, -80);
    var points = Enumerable.Repeat(point, count).ToList();
    return new()
    {
      Miles = 100,
      Seconds = 6000,
      Legs = [new(100, 6000, points)],
      Points = points,
    };
  }
}
