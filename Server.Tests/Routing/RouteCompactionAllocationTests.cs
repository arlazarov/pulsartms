using Domain.Models.Routing;
using Domain.Rules.Routing;
using Server.Tests.Support;
using Xunit.Abstractions;

namespace Server.Tests.Routing;

[Collection("Allocation measurements")]
[Trait("Category", "Routing")]
[Trait("Kind", "Allocation")]
public sealed class RouteCompactionAllocationTests(ITestOutputHelper output)
{
  [Fact]
  public void HundredStraightRouteIndexesHaveSmallerCompactBuffers()
  {
    var points = Enumerable
      .Range(0, 10_001)
      .Select(i => new RoutePoint(40, -100 + i * .0008))
      .ToList();
    var full = new TruckRoute { Legs = [new(500, 30000, points)] };
    var compact = new TruckRoute
    {
      Legs = [new(500, 30000, DisplayRouteGeometry.Simplify(points))],
    };
    GC.KeepAlive(new RouteGeometry(full));
    GC.KeepAlive(new RouteGeometry(compact));
    var (fullIndexes, fullBytes) = Build(full);
    var (compactIndexes, compactBytes) = Build(compact);
    for (var i = 0; i < 100; i++)
    {
      var position = points[i * 100];
      Assert.Equal(
        fullIndexes[i].Match(position).Along,
        compactIndexes[i].Match(position).Along,
        5
      );
    }
    output.WriteLine($"100 exact indexes: {fullBytes:N0} allocated bytes.");
    output.WriteLine(
      $"100 compact indexes: {compactBytes:N0} allocated bytes."
    );
    Assert.True(compactBytes < fullBytes / 10);
    GC.KeepAlive(fullIndexes);
    GC.KeepAlive(compactIndexes);
  }

  [Fact]
  public void SharedExactIndexAllocatesLessThanExpandedSegmentCopies()
  {
    var route = FuelGeometryFixture.RoundTrip(10001, 2);
    GC.KeepAlive(new ReferenceRouteGeometry(route));
    GC.KeepAlive(new RouteGeometry(route));
    var before = GC.GetAllocatedBytesForCurrentThread();
    var previous = new ReferenceRouteGeometry(route);
    var previousBytes = GC.GetAllocatedBytesForCurrentThread() - before;
    before = GC.GetAllocatedBytesForCurrentThread();
    var current = new RouteGeometry(route);
    var currentBytes = GC.GetAllocatedBytesForCurrentThread() - before;
    output.WriteLine($"Previous exact index: {previousBytes:N0} bytes.");
    output.WriteLine($"Shared exact index: {currentBytes:N0} bytes.");
    Assert.True(currentBytes < previousBytes * .6);
    Assert.Equal(RouteGeometry.EstimateBytes(route), current.EstimatedBytes);
    for (var mile = 0; mile <= 3000; mile += 50)
      Assert.True(
        RouteGeometry.Distance(previous.At(mile), current.At(mile)) < 1e-7
      );
    GC.KeepAlive(previous);
    GC.KeepAlive(current);
  }

  private static (RouteGeometry[] Indexes, long Bytes) Build(TruckRoute route)
  {
    var before = GC.GetAllocatedBytesForCurrentThread();
    var indexes = new RouteGeometry[100];
    for (var i = 0; i < indexes.Length; i++)
      indexes[i] = new RouteGeometry(route);
    return (indexes, GC.GetAllocatedBytesForCurrentThread() - before);
  }
}
