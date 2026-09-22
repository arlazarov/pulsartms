using Application.Features.Routing.Services.Routes;
using Domain.Models.Routing;

namespace Server.Tests.Routing;

[Trait("Category", "Routing")]
[Trait("Kind", "Unit")]
public sealed class PlanningSummaryCacheTests
{
  [Fact]
  public void BoardAndMapShareValuesButOnlyMapReadsGeometry()
  {
    var time = new FakeTimeProvider();
    var cache = new PlanningSummaryCache(time);
    var key = new PlanningSummaryCache.Key(Guid.NewGuid(), Guid.NewGuid());
    var plan = new RoutePlan
    {
      Id = Guid.NewGuid(),
      Version = 7,
      TruckId = key.Truck,
      Route = new()
      {
        Miles = 10,
        Legs = [new(10, 600, [new(40, -90), new(41, -90)])],
      },
    };
    cache.Read(key, "a");
    cache.Complete(
      cache.Take()!,
      "a",
      Result(key, time) with
      {
        State = new(new(), plan, null, 40, null, true),
      }
    );
    var board = cache.Read(key, "a", geometry: false)!;
    var map = cache.Read(key, "a")!;
    Assert.Equal(board.CalculatedAt, map.CalculatedAt);
    Assert.Equal(board.State!.FuelPercent, map.State!.FuelPercent);
    Assert.True(board.State.Plan!.GeometryOmitted);
    Assert.Empty(Assert.Single(board.State.Plan.Route.Legs).Points);
    Assert.Equal(2, Assert.Single(map.State.Plan!.Route.Legs).Points.Count);
    map.State.Plan.Route.Legs.Clear();
    Assert.Single(cache.Read(key, "a")!.State!.Plan!.Route.Legs);
    Assert.True(
      cache
        .Read(key, "a", true, plan.Id, plan.Version)!
        .State!.Plan!.GeometryOmitted
    );
  }

  [Fact]
  public void RequestsCoalesceAndFailedRefreshRetainsThePreviousResult()
  {
    var time = new FakeTimeProvider();
    var cache = new PlanningSummaryCache(time);
    var key = new PlanningSummaryCache.Key(Guid.NewGuid(), Guid.NewGuid());
    Assert.Null(cache.Read(key, "a"));
    var work = Assert.IsType<PlanningSummaryCache.Work>(cache.Take());
    Assert.Null(cache.Read(key, "a"));
    Assert.Null(cache.Take());
    cache.Complete(work, "a", Result(key, time));
    Assert.False(cache.Read(key, "a")!.IsRefreshing);
    time.Advance(TimeSpan.FromSeconds(31));
    Assert.True(cache.Read(key, "a")!.IsRefreshing);
    var refresh = Assert.IsType<PlanningSummaryCache.Work>(cache.Take());
    Assert.NotNull(cache.Read(key, "a"));
    cache.Complete(refresh, null, null);
    Assert.True(cache.Read(key, "a")!.IsRefreshing);
    Assert.Null(cache.Take());
  }

  [Fact]
  public void CompanyAndAssignmentChangesCannotReuseOrPublishOtherResults()
  {
    var time = new FakeTimeProvider();
    var cache = new PlanningSummaryCache(time);
    var key = new PlanningSummaryCache.Key(Guid.NewGuid(), Guid.NewGuid());
    cache.Read(key, "a");
    var work = cache.Take()!;
    cache.Read(key, "b");
    cache.Complete(work, "a", Result(key, time));
    Assert.Null(cache.Read(key, "b"));
    var current = cache.Take()!;
    cache.Complete(current, "b", Result(key, time));
    Assert.NotNull(cache.Read(key, "b"));
    Assert.Null(cache.Read(key with { Company = Guid.NewGuid() }, "b"));
  }

  [Fact]
  public void CacheIsBoundedAndEvictedWorkCannotRestoreAnEntry()
  {
    var time = new FakeTimeProvider();
    var cache = new PlanningSummaryCache(time);
    var first = new PlanningSummaryCache.Key(Guid.NewGuid(), Guid.NewGuid());
    cache.Read(first, "a");
    var old = cache.Take()!;
    for (var i = 0; i < 300; i++)
    {
      time.Advance(TimeSpan.FromMilliseconds(1));
      var key = new PlanningSummaryCache.Key(first.Company, Guid.NewGuid());
      cache.Read(key, "a");
      var work = cache.Take()!;
      cache.Complete(
        work,
        "a",
        Result(key, time) with
        {
          Message = new string('x', 100_000),
        }
      );
    }
    cache.Complete(old, "a", Result(first, time));
    var memory = Assert.Single(cache.ReadMemory());
    Assert.True(memory.Entries <= 256);
    Assert.True(memory.EstimatedSize <= memory.Limit);
    Assert.Null(cache.Read(first, "a"));
  }

  [Fact]
  public void InactiveTrucksStopRefreshing()
  {
    var time = new FakeTimeProvider();
    var cache = new PlanningSummaryCache(time);
    var key = new PlanningSummaryCache.Key(Guid.NewGuid(), Guid.NewGuid());
    cache.Read(key, "a");
    cache.Complete(cache.Take()!, "a", Result(key, time));
    time.Advance(TimeSpan.FromMinutes(3));
    Assert.Null(cache.Take());
    Assert.True(cache.Read(key, "a")!.IsRefreshing);
    Assert.NotNull(cache.Take());
  }

  [Fact]
  public void CommitRetainsDisplayAndRejectsEarlierInFlightWork()
  {
    var time = new FakeTimeProvider();
    var cache = new PlanningSummaryCache(time);
    var key = new PlanningSummaryCache.Key(Guid.NewGuid(), Guid.NewGuid());
    cache.Read(key, "a");
    cache.Complete(cache.Take()!, "a", Result(key, time));
    var old = cache.Capture(key, "a")!;
    cache.Committed(key.Company, key.Truck);
    Assert.False(cache.IsCurrent(old));
    Assert.True(cache.Read(key, "a")!.IsRefreshing);
    var fresh = cache.Take()!;
    cache.Complete(fresh, "a", Result(key, time) with { Message = "new" });
    cache.Complete(old, "a", Result(key, time) with { Message = "old" });
    Assert.Equal("new", cache.Read(key, "a")!.Message);
    Assert.False(cache.Read(key, "a")!.IsRefreshing);
  }

  [Fact]
  public void PublicationDoesNotMutateTheCalculationOrCrossCompanies()
  {
    var time = new FakeTimeProvider();
    var cache = new PlanningSummaryCache(time);
    var key = new PlanningSummaryCache.Key(Guid.NewGuid(), Guid.NewGuid());
    var result = Result(key, time) with
    {
      State = new(
        new(),
        new RoutePlan
        {
          Route = new() { Legs = [new(10, 60, [new(40, -90)])] },
        },
        null,
        40,
        null,
        true
      ),
    };
    cache.Read(key, "a");
    cache.Complete(cache.Take()!, "a", result);
    Assert.Single(result.State.Plan!.Route.Legs[0].Points);
    Assert.Empty(
      cache.CaptureDispatch(Guid.NewGuid(), result.DispatchId!.Value)
    );
    var work = Assert.Single(
      cache.CaptureDispatch(key.Company, result.DispatchId.Value)
    );
    cache.Committed(Guid.NewGuid(), key.Truck);
    Assert.True(cache.IsCurrent(work));
    cache.Complete(work, "a", result);
    Assert.False(cache.IsCurrent(work));
  }

  private sealed class FakeTimeProvider : TimeProvider
  {
    private DateTimeOffset now = DateTimeOffset.UtcNow;

    public override DateTimeOffset GetUtcNow() => now;

    public void Advance(TimeSpan duration) => now += duration;
  }

  private static AutomaticPlanningResult Result(
    PlanningSummaryCache.Key key,
    TimeProvider time
  ) =>
    new(key.Truck, Guid.NewGuid(), 1, null, null)
    {
      CalculatedAt = time.GetUtcNow(),
    };
}
