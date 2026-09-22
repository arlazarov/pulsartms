using Application.Features.Fleet.Queries.GetFleetLocations;
using Domain.Models.Fleet;
using Server.Tests.Support;

namespace Server.Tests.Fleet;

[Trait("Category", "Fleet")]
[Trait("Kind", "Unit")]
public class TruckHistoryCacheTests
{
  [Fact]
  public void HistoryIsIsolatedByCompany()
  {
    var companies = new TestCompany();
    using var cache = new TruckHistoryCache(companies);
    var snapshot = new TruckHistoryCache.Snapshot(
      [],
      DateTime.UtcNow,
      DateTime.UtcNow
    );
    cache.Set("truck-window", snapshot);
    using (companies.As(Guid.NewGuid()))
      Assert.Null(cache.Get("truck-window"));
    Assert.Same(snapshot, cache.Get("truck-window"));
  }

  [Fact]
  public void OversizedHistoryDoesNotReplaceSavedSnapshot()
  {
    using var cache = new TruckHistoryCache(new TestCompany());
    var saved = new TruckHistoryCache.Snapshot(
      [],
      DateTime.UtcNow,
      DateTime.UtcNow
    );
    cache.Set("truck-window", saved);
    var points = new[]
    {
      new VehicleLocationPoint
      {
        FormattedLocation = new string(
          'x',
          (int)TruckHistoryCache.Capacity / 2
        ),
      },
    };
    cache.Set("truck-window", saved with { Points = points });
    Assert.Same(saved, cache.Get("truck-window"));
  }

  [Fact]
  public void ManyWindowsCannotExceedTheSharedBudget()
  {
    using var cache = new TruckHistoryCache(new TestCompany());
    var points = Enumerable
      .Range(0, 2000)
      .Select(_ => new VehicleLocationPoint())
      .ToArray();
    var keys = Enumerable.Range(0, 100).Select(i => $"window-{i}").ToArray();
    foreach (var key in keys)
      cache.Set(key, new(points, DateTime.UtcNow, DateTime.UtcNow));
    var retained = keys.Count(key => cache.Get(key) is not null);
    Assert.InRange(retained, 0, 21);
  }
}
