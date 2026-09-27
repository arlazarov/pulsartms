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

  // Running work is prepared with nobody reading it: the first reader, even
  // on a cold start, opens a summary that is already there, and one that
  // nobody has looked at for more than ten minutes is still being kept.
  [Fact]
  public void RunningWorkIsPreparedWithoutAReaderAndKeptWithoutOne()
  {
    var time = new FakeTimeProvider();
    var cache = new PlanningSummaryCache(time);
    var key = new PlanningSummaryCache.Key(Guid.NewGuid(), Guid.NewGuid());

    cache.Keep(key, "a");
    cache.Complete(cache.Take()!, "a", Result(key, time));
    var opened = cache.Read(key, "a")!;
    Assert.False(opened.IsRefreshing);

    // Twelve minutes, no reader, a background pass every thirty seconds.
    var prepared = 1;
    for (var elapsed = 0; elapsed < 24; elapsed++)
    {
      time.Advance(TimeSpan.FromSeconds(30));
      cache.Keep(key, "a");
      if (cache.Take() is { } work)
      {
        cache.Complete(work, "a", Result(key, time));
        prepared++;
      }
    }
    Assert.Equal(25, prepared);
    Assert.False(cache.Read(key, "a")!.IsRefreshing);
  }

  // Asking again for the same work is not a reason to prepare it again:
  // it is prepared on the freshness interval, however often it is asked.
  [Fact]
  public void RepeatedDemandDoesNotRepeatWork()
  {
    var time = new FakeTimeProvider();
    var cache = new PlanningSummaryCache(time);
    var key = new PlanningSummaryCache.Key(Guid.NewGuid(), Guid.NewGuid());
    cache.Keep(key, "a");
    cache.Complete(cache.Take()!, "a", Result(key, time));
    for (var i = 0; i < 20; i++)
    {
      cache.Keep(key, "a");
      cache.Read(key, "a", geometry: false);
    }
    Assert.Null(cache.Take());
    time.Advance(TimeSpan.FromSeconds(30));
    Assert.NotNull(cache.Take());
    Assert.Null(cache.Take());
  }

  // A committed change to one truck makes that truck due, and only it.
  [Fact]
  public void OneTrucksChangeLeavesTheRestOfTheFleetAlone()
  {
    var time = new FakeTimeProvider();
    var cache = new PlanningSummaryCache(time);
    var company = Guid.NewGuid();
    var changed = new PlanningSummaryCache.Key(company, Guid.NewGuid());
    var fleet = Enumerable
      .Range(0, 5)
      .Select(_ => new PlanningSummaryCache.Key(company, Guid.NewGuid()))
      .ToArray();
    foreach (var key in fleet.Append(changed))
    {
      cache.Keep(key, "a");
      cache.Complete(cache.Take()!, "a", Result(key, time));
    }

    var committed = Assert.Single(cache.Committed(company, changed.Truck));

    Assert.Equal(changed, committed.Key);
    Assert.Equal(changed, cache.Take()!.Key);
    Assert.Null(cache.Take());
    Assert.All(fleet, key => Assert.False(cache.Read(key, "a")!.IsRefreshing));
  }

  // A new assignment is new work: the background asking for it does not
  // carry the old snapshot across, and a reader of the new work sees none.
  [Fact]
  public void BackgroundDemandNeverCarriesASnapshotToOtherWork()
  {
    var time = new FakeTimeProvider();
    var cache = new PlanningSummaryCache(time);
    var key = new PlanningSummaryCache.Key(Guid.NewGuid(), Guid.NewGuid());
    cache.Keep(key, "first assignment");
    cache.Complete(cache.Take()!, "first assignment", Result(key, time));

    cache.Keep(key, "second assignment");

    Assert.Null(cache.Read(key, "second assignment"));
    var work = cache.Take()!;
    Assert.Equal("second assignment", work.Signature);
    cache.Complete(work, "first assignment", Result(key, time));
    Assert.Null(cache.Read(key, "second assignment"));
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
    // The earlier work still holds the entry: the commit waits for it
    // rather than starting a second preparation alongside it.
    Assert.Null(cache.Take());
    cache.Complete(old, "a", Result(key, time) with { Message = "old" });
    Assert.NotEqual("old", cache.Read(key, "a")!.Message);
    var fresh = cache.Take()!;
    cache.Complete(fresh, "a", Result(key, time) with { Message = "new" });
    Assert.Equal("new", cache.Read(key, "a")!.Message);
    Assert.False(cache.Read(key, "a")!.IsRefreshing);
  }

  // Overlap, counted: commits and other inputs arriving while one
  // preparation runs make one more preparation after it - not one each,
  // and none alongside it.
  [Fact]
  public void ChangesDuringAPreparationAddOnePreparationAfterIt()
  {
    var time = new FakeTimeProvider();
    var cache = new PlanningSummaryCache(time);
    var key = new PlanningSummaryCache.Key(Guid.NewGuid(), Guid.NewGuid());
    cache.Read(key, "a");
    var preparations = new List<PlanningSummaryCache.Work> { cache.Take()! };

    cache.Committed(key.Company, key.Truck);
    preparations.AddRange(Taken());
    cache.Committed(key.Company, key.Truck);
    cache.Read(key, "b");
    preparations.AddRange(Taken());
    cache.Committed(key.Company, key.Truck);
    preparations.AddRange(Taken());
    Assert.Single(preparations);

    cache.Complete(preparations[0], "a", Result(key, time));
    Assert.Null(cache.Read(key, "b"));
    preparations.AddRange(Taken());
    Assert.Equal(2, preparations.Count);
    Assert.Equal("b", preparations[1].Signature);
    cache.Complete(preparations[1], "b", Result(key, time));
    Assert.False(cache.Read(key, "b")!.IsRefreshing);
    Assert.Empty(Taken());

    IEnumerable<PlanningSummaryCache.Work> Taken() =>
      cache.Take() is { } work ? [work] : [];
  }

  // The route refresh publishes what it prepared into entries it captured,
  // taking them from a consumer that was preparing them. That consumer's
  // work is lost - two computations, not one - but nothing runs alongside
  // the refresh after its own commit, and the consumer's late result is not
  // stored over the refresh's.
  [Fact]
  public void ARouteRefreshCaptureOverlappingAConsumerIsCountedNotCoalesced()
  {
    var time = new FakeTimeProvider();
    var cache = new PlanningSummaryCache(time);
    var key = new PlanningSummaryCache.Key(Guid.NewGuid(), Guid.NewGuid());
    cache.Read(key, "a");
    var consumer = cache.Take()!;
    var refresh = cache.Capture(key, "a")!;
    var committed = Assert.Single(cache.Committed(key.Company, key.Truck));
    Assert.Equal(refresh.Lease, committed.Lease);
    Assert.Null(cache.Take());

    cache.Complete(
      committed,
      "a",
      Result(key, time) with
      {
        Message = "refresh",
      }
    );
    cache.Complete(
      consumer,
      "a",
      Result(key, time) with
      {
        Message = "consumer",
      }
    );
    cache.Complete(refresh, null, null);

    Assert.Equal("refresh", cache.Read(key, "a")!.Message);
    Assert.Null(cache.Take());
  }

  // A preparation whose holder never completes stops holding the entry.
  [Fact]
  public void AnAbandonedPreparationIsTakenAgainAfterItsLease()
  {
    var time = new FakeTimeProvider();
    var cache = new PlanningSummaryCache(time);
    var key = new PlanningSummaryCache.Key(Guid.NewGuid(), Guid.NewGuid());
    cache.Read(key, "a");
    var abandoned = cache.Take()!;
    cache.Committed(key.Company, key.Truck);
    time.Advance(TimeSpan.FromMinutes(1));
    cache.Read(key, "a");
    Assert.Null(cache.Take());
    time.Advance(TimeSpan.FromMinutes(5));
    cache.Read(key, "a");
    var again = cache.Take()!;
    Assert.NotEqual(abandoned.Lease, again.Lease);
    cache.Complete(abandoned, "a", Result(key, time) with { Message = "old" });
    Assert.Null(cache.Take());
    cache.Complete(again, "a", Result(key, time) with { Message = "new" });
    Assert.Equal("new", cache.Read(key, "a")!.Message);
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

  // Nothing changed while an abandoned preparation ran; its lease ran out
  // and another took the entry over. The first one finishing late may
  // neither publish nor end the replacement's lease.
  [Fact]
  public void AnExpiredPreparationCannotPublishOverItsReplacement()
  {
    var time = new FakeTimeProvider();
    var cache = new PlanningSummaryCache(time);
    var key = new PlanningSummaryCache.Key(Guid.NewGuid(), Guid.NewGuid());
    cache.Read(key, "a");
    var expired = cache.Take()!;
    time.Advance(TimeSpan.FromMinutes(6));
    cache.Read(key, "a");
    var replacement = cache.Take()!;

    Assert.False(cache.IsCurrent(expired));
    cache.Complete(expired, "a", Result(key, time) with { Message = "old" });
    Assert.Null(cache.Read(key, "a"));
    Assert.Null(cache.Take());
    cache.Complete(
      replacement,
      "a",
      Result(key, time) with
      {
        Message = "new",
      }
    );
    Assert.Equal("new", cache.Read(key, "a")!.Message);
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

  // The background signs with the truck's work read fresh; the reader asks
  // with the read cache's copy, which can lag it. A result signed with the
  // fresh work is not stored for a reader asking with the older copy - so
  // older work is never shown under the newer work's signature, nor the
  // other way round (audit, September 26). Once the reader's copy catches
  // up, the next calculation is stored.
  [Fact]
  public void AResultSignedWithFresherWorkWaitsForTheReaderToAskForIt()
  {
    var time = new ManualTimeProvider(DateTimeOffset.UtcNow);
    var cache = new PlanningSummaryCache(time);
    var key = new PlanningSummaryCache.Key(Guid.NewGuid(), Guid.NewGuid());
    Assert.Null(cache.Read(key, "work:cached"));
    var work = cache.Take()!;

    cache.Complete(
      work,
      "work:fresh",
      new(key.Truck, Guid.NewGuid(), 1408, null, null)
      {
        CalculatedAt = time.GetUtcNow(),
      }
    );

    // Not prepared again while nobody asks for the fresher work: that
    // would repeat the same mismatch.
    Assert.Null(cache.Take());
    Assert.Null(cache.Read(key, "work:cached"));
    Assert.Null(cache.Take());
    Assert.Null(cache.Read(key, "work:fresh"));
    var again = cache.Take()!;
    Assert.Equal("work:fresh", again.Signature);
    cache.Complete(
      again,
      "work:fresh",
      new(key.Truck, Guid.NewGuid(), 1408, null, null)
      {
        CalculatedAt = time.GetUtcNow(),
      }
    );
    Assert.Equal(1408, cache.Read(key, "work:fresh")!.LoadNumber);
  }
}
