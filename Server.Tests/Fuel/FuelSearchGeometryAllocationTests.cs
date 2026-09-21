using Domain.Rules.Routing;
using Server.Tests.Support;
using Xunit.Abstractions;

namespace Server.Tests.Fuel;

[Collection("Allocation measurements")]
[Trait("Category", "Fuel")]
[Trait("Kind", "Allocation")]
public sealed class FuelSearchGeometryAllocationTests(ITestOutputHelper output)
{
  [Theory]
  [InlineData(25_001)]
  [InlineData(50_001)]
  public void DenseRouteAddsABoundedIndexAndRefinesOnlyNearbyOriginalSegments(
    int pointsPerLeg
  )
  {
    var route = FuelGeometryFixture.RoundTrip(pointsPerLeg, 2);
    GC.KeepAlive(new FuelSearchGeometry(route));
    var before = GC.GetAllocatedBytesForCurrentThread();
    var search = new FuelSearchGeometry(route);
    var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
    Assert.InRange(
      search.BlockCount,
      1,
      FuelSearchGeometry.TargetBlockCount + route.Legs.Count
    );
    Assert.True(
      allocated < 180_000,
      $"Additional search index allocation: {allocated:N0} bytes."
    );
    var positions = Enumerable
      .Range(1, 40)
      .Select(i =>
        route.Legs[0].Points[i * (pointsPerLeg - 1) / 41] with
        {
          Latitude =
            route.Legs[0].Points[i * (pointsPerLeg - 1) / 41].Latitude + .001,
        }
      )
      .ToArray();
    search.Match(positions[0]);
    before = GC.GetAllocatedBytesForCurrentThread();
    var mostExamined = 0;
    foreach (var point in positions)
      mostExamined = Math.Max(
        mostExamined,
        search.Match(point).SegmentsExamined
      );
    var queryBytes = GC.GetAllocatedBytesForCurrentThread() - before;
    Assert.True(
      queryBytes < 4096,
      $"Per-query refinement allocated point buffers: {queryBytes:N0} bytes."
    );
    Assert.InRange(mostExamined, 1, 1000);
    Assert.Equal(pointsPerLeg * 2, route.Legs.Sum(leg => leg.Points.Count));
    Assert.Equal(3000, route.Miles);
    Assert.Equal(180246, route.Seconds);
    output.WriteLine(
      $"Original route remains resident: {pointsPerLeg * 2:N0} leg points. Additional index: {search.BlockCount:N0} blocks, {allocated:N0} allocated bytes. Forty matches: {queryBytes:N0} allocated bytes; at most {mostExamined:N0} original segments refined per match."
    );
    GC.KeepAlive(route);
    GC.KeepAlive(search);
  }
}
