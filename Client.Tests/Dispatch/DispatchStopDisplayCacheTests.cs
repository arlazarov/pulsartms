using Client.Models.DTO.Dispatch;
using Client.Shared.Dispatch;

namespace Client.Tests.Dispatch;

[Trait("Category", "Dispatch")]
[Trait("Kind", "Unit")]
public sealed class DispatchStopDisplayCacheTests
{
  [Fact]
  public void TelemetryOnlyPassReusesOrderVisitsAndSummaryButInPlaceStopChangesRebuildThem()
  {
    var first = new DispatchStopResponse
    {
      Id = Guid.NewGuid(),
      Sequence = 2,
      Job = "Delivery",
      Address = "100 Main",
      City = "Toronto",
      Province = "ON",
    };
    var second = new DispatchStopResponse
    {
      Id = Guid.NewGuid(),
      Sequence = 1,
      Job = "Pickup",
      Address = "100 Main",
      City = "Toronto",
      Province = "ON",
    };
    DispatchStopResponse[] stops = [first, second];
    var cache = new DispatchStopDisplayCache();
    cache.Update(stops);
    var ordered = cache.OrderedStops;
    var visits = cache.Visits;
    var summary = cache.Summary;
    cache.Update(stops);
    Assert.Same(ordered, cache.OrderedStops);
    Assert.Same(visits, cache.Visits);
    Assert.Same(summary, cache.Summary);
    Assert.Equal(2, visits[0].VisitCount);
    first.Address = "200 Main";
    first.Sequence = 0;
    cache.Update(stops);
    Assert.NotSame(ordered, cache.OrderedStops);
    Assert.Same(first, cache.OrderedStops[0]);
    Assert.All(cache.Visits, visit => Assert.Equal(1, visit.VisitCount));
  }

  [Fact]
  public void ANewPayloadRebindsPresentationToTheCurrentStopInstances()
  {
    var first = new DispatchStopResponse { Id = Guid.NewGuid(), Sequence = 1 };
    var cache = new DispatchStopDisplayCache();
    cache.Update([first]);
    var next = new DispatchStopResponse
    {
      Id = first.Id,
      Sequence = 1,
      ScheduledDate = new(2026, 9, 12),
    };
    cache.Update([next]);
    Assert.Same(next, cache.Visits[0].Stop);
  }
}
