using Application.Features.Eta.Interfaces;
using Application.Features.Eta.Services;
using Domain.Models.Routing;
using Domain.Rules.Eta;
using Domain.Rules.Ports;

namespace Server.Tests.Eta;

[Trait("Category", "Eta")]
[Trait("Kind", "Unit")]
public sealed class EtaRouteTimingTests
{
  [Fact]
  public void SameVersionReusesCompiledRegionsAndNewVersionCompilesAgain()
  {
    var cache = new EtaRouteTimingCache();
    var lookup = new CountingRegions();
    var id = Guid.NewGuid();
    var route = Route();
    var first = cache.GetOrCreate(id, 1, route, lookup);
    var calls = lookup.Calls;
    Assert.Equal(50, calls);
    Assert.Same(first, cache.GetOrCreate(id, 1, Route(), lookup));
    Assert.Equal(calls, lookup.Calls);
    Assert.NotSame(first, cache.GetOrCreate(id, 2, route, lookup));
    Assert.Equal(calls * 2, lookup.Calls);
  }

  [Fact]
  public void CompilesInteriorCountryChangesEvenWhenEndpointsShareCountry()
  {
    var lookup = new CountingRegions(point =>
      point.Longitude is > -80.7 and < -80.3
        ? new("CA", "Etc/UTC", false)
        : new("US", "Etc/UTC", false)
    );
    var timing = EtaRouteTiming.Compile(Route(), lookup);
    var leg = Assert.Single(timing.Legs);
    Assert.True(timing.HasCompleteTravelTimes);
    Assert.Equal(
      ["US", "CA", "US"],
      leg.Segments.Select(segment => segment.Country)
    );
    Assert.Equal(0, leg.Segments[0].StartMiles);
    Assert.Equal(100, leg.Segments[^1].EndMiles);
    Assert.Equal(
      100,
      leg.Segments.Sum(segment => segment.EndMiles - segment.StartMiles),
      8
    );
  }

  [Fact]
  public void PreservesLegTimingBoundariesAndUnsupportedRegions()
  {
    var route = Route();
    route.Legs.Add(new(40, 4800, [new(35, -80), new(35, -79)]));
    var lookup = new CountingRegions(point =>
      point.Longitude < -80
        ? new("US", "Etc/UTC", false)
        : new("CA", "Etc/UTC", true)
    );
    var timing = EtaRouteTiming.Compile(route, lookup);
    Assert.Equal(2, timing.Legs.Length);
    Assert.Equal(7200, timing.Legs[0].Seconds);
    Assert.Equal(100, timing.Legs[1].StartMiles);
    Assert.Equal(140, timing.Legs[1].EndMiles);
    Assert.Equal(4800, timing.Legs[1].Seconds);
    Assert.True(Assert.Single(timing.Legs[0].Segments).IsSupported);
    Assert.False(Assert.Single(timing.Legs[1].Segments).IsSupported);
    var unknown = EtaRouteTiming.Compile(
      Route(),
      new CountingRegions(_ => new("", "Etc/UTC", false))
    );
    Assert.False(
      Assert.Single(Assert.Single(unknown.Legs).Segments).IsSupported
    );
  }

  [Fact]
  public void SamplingKeepsInteriorVerticesAndAtMostTwoMilesPerSample()
  {
    var sampled = new List<RoutePoint>();
    var route = new TruckRoute
    {
      Legs = [new(10, 1200, [new(35, -82), new(35, -81), new(35, -80)])],
    };
    var timing = EtaRouteTiming.Compile(
      route,
      new CountingRegions(point =>
      {
        sampled.Add(point);
        return new("US", "Etc/UTC", false);
      })
    );
    Assert.Equal(6, sampled.Count);
    Assert.Equal(-82 + 1d / 6, sampled[0].Longitude, 8);
    Assert.Equal(-81 + 1d / 6, sampled[3].Longitude, 8);
    Assert.Single(Assert.Single(timing.Legs).Segments);
  }

  [Fact]
  public void ChangedLegMetadataDoesNotReuseCollidingPlanKey()
  {
    var cache = new EtaRouteTimingCache();
    var lookup = new CountingRegions();
    var id = Guid.NewGuid();
    var first = cache.GetOrCreate(id, 1, Route(), lookup);
    var changed = Route();
    changed.Legs[0] = changed.Legs[0] with { Seconds = 10800 };
    var next = cache.GetOrCreate(id, 1, changed, lookup);
    Assert.NotSame(first, next);
    Assert.Equal(10800, next.Legs[0].Seconds);
    Assert.Equal(1, cache.Count);
  }

  [Fact]
  public void CacheEvictsLeastRecentlyUsedAndBoundsRetainedUnits()
  {
    var cache = new EtaRouteTimingCache(capacity: 2, maximumRetainedUnits: 4);
    var lookup = new CountingRegions();
    var firstId = Guid.NewGuid();
    var secondId = Guid.NewGuid();
    var first = cache.GetOrCreate(firstId, 1, Route(), lookup);
    var second = cache.GetOrCreate(secondId, 1, Route(), lookup);
    Assert.Same(first, cache.GetOrCreate(firstId, 1, Route(), lookup));
    cache.GetOrCreate(Guid.NewGuid(), 1, Route(), lookup);
    Assert.Equal(2, cache.Count);
    Assert.Equal(4, cache.RetainedUnits);
    Assert.Same(first, cache.GetOrCreate(firstId, 1, Route(), lookup));
    Assert.NotSame(second, cache.GetOrCreate(secondId, 1, Route(), lookup));
    Assert.InRange(cache.RetainedUnits, 0, 4);
  }

  [Fact]
  public async Task ConcurrentRequestsCompileTheSameVersionOnce()
  {
    var cache = new EtaRouteTimingCache();
    var lookup = new CountingRegions();
    var id = Guid.NewGuid();
    var route = Route();
    var results = await Task.WhenAll(
      Enumerable
        .Range(0, 12)
        .Select(_ => Task.Run(() => cache.GetOrCreate(id, 1, route, lookup)))
    );
    Assert.All(results, result => Assert.Same(results[0], result));
    Assert.Equal(50, lookup.Calls);
  }

  [Fact]
  public void OversizedProfilesAreReturnedWithoutExceedingCacheBound()
  {
    var cache = new EtaRouteTimingCache(maximumRetainedUnits: 1);
    var result = cache.GetOrCreate(
      Guid.NewGuid(),
      1,
      Route(),
      new CountingRegions()
    );
    Assert.True(result.HasCompleteTravelTimes);
    Assert.Equal(0, cache.Count);
    Assert.Equal(0, cache.RetainedUnits);
  }

  [Fact]
  public void MissingTravelTimesRemainUnavailable()
  {
    var route = Route();
    route.Legs[0] = route.Legs[0] with { Seconds = 0 };
    var lookup = new CountingRegions();
    var timing = EtaRouteTiming.Compile(route, lookup);
    Assert.False(timing.HasCompleteTravelTimes);
    Assert.Empty(Assert.Single(timing.Legs).Segments);
    Assert.Equal(0, lookup.Calls);
  }

  [Fact]
  public void DegenerateGeometryDoesNotBecomeAZeroHourSuccessfulEstimate()
  {
    var route = Route();
    route.Legs[0] = route.Legs[0] with
    {
      Points = [new(35, -81), new(35, -81)],
    };
    var timing = EtaRouteTiming.Compile(route, new CountingRegions());
    Assert.False(timing.HasCompleteTravelTimes);
    Assert.Empty(Assert.Single(timing.Legs).Segments);
  }

  [Fact]
  public void VerifiedZeroDistanceAndDurationPreserveACoLocatedStop()
  {
    var route = new TruckRoute
    {
      Legs = [new(0, 0, [new(35, -81), new(35, -81)])],
    };
    var timing = EtaRouteTiming.Compile(route, new CountingRegions());
    Assert.True(timing.HasCompleteTravelTimes);
    Assert.Empty(Assert.Single(timing.Legs).Segments);
    Assert.Equal(0, timing.Legs[0].EndMiles);
  }

  private static TruckRoute Route() =>
    new()
    {
      Miles = 100,
      Seconds = 7200,
      Legs = [new(100, 7200, [new(35, -81), new(35, -80)])],
    };

  private sealed class CountingRegions(
    Func<RoutePoint, RouteRegion>? resolve = null
  ) : IRouteRegionLookup
  {
    private int calls;
    public int Calls => calls;

    public RouteRegion Find(RoutePoint point)
    {
      Interlocked.Increment(ref calls);
      return resolve?.Invoke(point) ?? new("US", "Etc/UTC", false);
    }
  }
}
