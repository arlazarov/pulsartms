using Domain.Models.Eta;
using Infrastructure.Integrations.Samsara;

namespace Server.Tests.Fleet;

[Trait("Category", "Fleet")]
[Trait("Kind", "Unit")]
public sealed class SamsaraHosCacheTests
{
  [Fact]
  public void IdleHistoryExpiresAndGateAllocationIsBounded()
  {
    var clock = new ManualTimeProvider();
    using var cache = new SamsaraHosHistoryCache(clock, new TestCompany());
    cache.Store(
      "driver",
      new(null, clock.GetUtcNow(), clock.GetUtcNow(), false)
    );
    Assert.NotNull(cache.Get("driver"));
    clock.Advance(TimeSpan.FromMinutes(30));
    Assert.Null(cache.Get("driver"));
    Assert.InRange(
      Enumerable
        .Range(0, 1024)
        .Select(i => cache.Gate(i.ToString()))
        .Distinct()
        .Count(),
      1,
      64
    );
  }

  [Fact]
  public void OversizedHistoryIsNotRetained()
  {
    var clock = new ManualTimeProvider();
    using var cache = new SamsaraHosHistoryCache(clock, new TestCompany());
    var now = clock.GetUtcNow();
    var history = new HosHistory(
      now.AddDays(-16),
      now,
      "Etc/UTC",
      0,
      null,
      null,
      [new(now.AddDays(-16), now, new string('x', 8 * 1024 * 1024))]
    );
    cache.Store("driver", new(history, now, now, true));
    Assert.True(cache.Get("driver") is null);
  }

  [Fact]
  public void CachedPeriodsCannotBeChangedByTheSourceListOrAReader()
  {
    var clock = new ManualTimeProvider();
    using var cache = new SamsaraHosHistoryCache(clock, new TestCompany());
    var now = clock.GetUtcNow();
    var periods = new List<HosPeriod> { new(now.AddDays(-1), now, "onDuty") };
    var history = new HosHistory(
      now.AddDays(-1),
      now,
      "Etc/UTC",
      0,
      null,
      null,
      periods
    );
    cache.Store("driver", new(history, now, now, true));
    periods.Clear();
    var saved = cache.Get("driver")!.Baseline!;
    Assert.Single(saved.Periods);
    Assert.Throws<NotSupportedException>(
      () => ((IList<HosPeriod>)saved.Periods).Clear()
    );
    Assert.Single(cache.Get("driver")!.Baseline!.Periods);
  }
}
