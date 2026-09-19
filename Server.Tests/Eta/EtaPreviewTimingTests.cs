using Application.Features.Eta.Algorithms;
using Application.Features.Eta.Interfaces;
using Application.Features.Eta.Options;
using Application.Features.Eta.Services;
using Application.Features.Fleet.Models;
using Application.Features.Routing.Models;
using Microsoft.Extensions.Options;

namespace Server.Tests.Eta;

[Trait("Category", "Eta")]
[Trait("Kind", "Unit")]
public sealed class EtaPreviewTimingTests
{
  [Fact]
  public void DenseGeometryUsesUniformMileageSamplesAndRetainsInteriorCountryTransitions()
  {
    var route = Road(100, 100_000);
    var regions = new CountingRegions(point =>
      point.Longitude is > -80.7 and < -80.3
        ? new("CA", "Etc/UTC", false)
        : new("US", "Etc/UTC", false)
    );
    var timing = EtaRouteTiming.CompilePreview(route, regions);
    Assert.NotNull(timing);
    Assert.True(timing.HasCompleteTravelTimes);
    Assert.Equal(50, regions.Calls);
    var leg = Assert.Single(timing.Legs);
    Assert.Equal(route.Miles, leg.Miles);
    Assert.Equal(route.Seconds, leg.Seconds);
    Assert.Equal(
      new[] { "US", "CA", "US" },
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
  public void SampleBudgetIsSharedByAllLegsAndExceededRoutesDoNotStartLookup()
  {
    var route = Road(5000, 2);
    route.Legs.Add(new(5000, 300000, [new(35, -80), new(35, -79)]));
    route.Miles = 10000;
    route.Seconds = 600000;
    var regions = new CountingRegions();
    Assert.NotNull(EtaRouteTiming.CompilePreview(route, regions));
    Assert.Equal(EtaRouteTiming.MaximumPreviewSamples, regions.Calls);
    route.Legs[1] = route.Legs[1] with { Miles = 5000.1 };
    route.Miles = 10000.1;
    var overBudget = new CountingRegions();
    Assert.Null(EtaRouteTiming.CompilePreview(route, overBudget));
    Assert.Equal(0, overBudget.Calls);
  }

  [Fact]
  public void ZeroLegsKeepBoundariesWithoutInventingTravelAndUnknownRegionsStayUnsupported()
  {
    var a = new RoutePoint(35, -81);
    var b = new RoutePoint(35, -80);
    var route = new TruckRoute
    {
      Miles = 4,
      Seconds = 240,
      Legs = [new(0, 0, [a, a]), new(4, 240, [a, b]), new(0, 0, [b, b])],
    };
    var regions = new CountingRegions(_ => new("", "Etc/UTC", false));
    var timing = EtaRouteTiming.CompilePreview(route, regions);
    Assert.NotNull(timing);
    Assert.True(timing.HasCompleteTravelTimes);
    Assert.Equal(
      new[] { 0d, 0d, 4d },
      timing.Legs.Select(leg => leg.StartMiles)
    );
    Assert.Empty(timing.Legs[0].Segments);
    Assert.Empty(timing.Legs[2].Segments);
    Assert.False(Assert.Single(timing.Legs[1].Segments).IsSupported);
    Assert.Equal(2, regions.Calls);
  }

  [Theory]
  [InlineData("points")]
  [InlineData("missing-time")]
  [InlineData("degenerate")]
  [InlineData("invalid-coordinate")]
  [InlineData("moving-zero-leg")]
  public void InvalidPreviewGeometryRemainsUnavailable(string invalid)
  {
    var route = Road(100, invalid == "points" ? 100_001 : 2);
    if (invalid == "missing-time")
    {
      route.Legs[0] = route.Legs[0] with { Seconds = 0 };
      route.Seconds = 0;
    }
    if (invalid == "degenerate")
      route.Legs[0].Points[1] = route.Legs[0].Points[0];
    if (invalid == "invalid-coordinate")
      route.Legs[0].Points[1] = new(double.NaN, -80);
    if (invalid == "moving-zero-leg")
    {
      route.Legs[0] = route.Legs[0] with { Miles = 0, Seconds = 0 };
      route.Miles = route.Seconds = 0;
    }
    Assert.Null(EtaRouteTiming.CompilePreview(route, new CountingRegions()));
  }

  [Fact]
  public void CancellationStopsRegionalSampling()
  {
    using var cancellation = new CancellationTokenSource();
    var regions = new CountingRegions(_ =>
    {
      cancellation.Cancel();
      return new("US", "Etc/UTC", false);
    });
    Assert.Throws<OperationCanceledException>(
      () =>
        EtaRouteTiming.CompilePreview(
          Road(100, 100_000),
          regions,
          cancellation.Token
        )
    );
    Assert.Equal(1, regions.Calls);
    Assert.Throws<OperationCanceledException>(
      () =>
        EtaRouteTiming.CompilePreview(Road(100, 2), regions, cancellation.Token)
    );
    Assert.Equal(1, regions.Calls);
  }

  [Fact]
  public void PreviewReplayDoesNotPopulateOrEvictLiveTimingCache()
  {
    using var memory = new EtaMemory();
    var regions = new CountingRegions();
    var liveId = Guid.NewGuid();
    var liveRoad = Road(10, 2);
    var live = memory.Timing.GetOrCreate(liveId, 1, liveRoad, regions);
    var count = memory.Timing.Count;
    var retained = memory.Timing.RetainedUnits;
    var before = regions.Calls;
    var now = new DateTime(2026, 9, 8, 16, 0, 0, DateTimeKind.Utc);
    var road = Road(100, 100_000);
    var plan = new RoutePlan
    {
      Id = Guid.NewGuid(),
      Version = 1,
      DispatchId = Guid.NewGuid(),
      FromCurrentPosition = true,
      Route = road,
      Stops = [new(Guid.NewGuid(), "Delivery", "", 1, road.Legs[0].Points[^1])],
    };
    var state = new RoutePlanningState(
      new(),
      plan,
      new(
        0,
        road.Miles,
        road.Seconds,
        0,
        false,
        false,
        now,
        road.Legs[0].Points[0]
      ),
      50,
      now,
      true
    );
    var service = new EtaService(
      null!,
      null!,
      regions,
      memory,
      new PlanningTestServices.NoHos(),
      Options.Create(new EtaPlanningOptions())
    );
    var clocks = new DriverHosClocks
    {
      DriveMs = 11 * 3600000L,
      ShiftMs = 14 * 3600000L,
      BreakMs = 8 * 3600000L,
      CycleMs = 70 * 3600000L,
      UpdatedAt = now,
    };
    var result = service.CalculateRoadPreview(state, clocks, now);
    Assert.Single(result.Stops);
    Assert.InRange(regions.Calls - before, 50, 52);
    Assert.Equal(count, memory.Timing.Count);
    Assert.Equal(retained, memory.Timing.RetainedUnits);
    Assert.Same(live, memory.Timing.GetOrCreate(liveId, 1, liveRoad, regions));
    Assert.Empty(memory.Results);
    Assert.Empty(memory.Viewed);

    plan.Route = Road(10001, 2);
    var unavailable = service.CalculateRoadPreview(state, clocks, now);
    Assert.Empty(unavailable.Stops);
    Assert.Contains("bounds", unavailable.UnavailableReason);
    Assert.Equal(count, memory.Timing.Count);
  }

  private static TruckRoute Road(double miles, int points) =>
    new()
    {
      Miles = miles,
      Seconds = miles * 60,
      Legs =
      [
        new(
          miles,
          miles * 60,
          Enumerable
            .Range(0, points)
            .Select(i => new RoutePoint(35, -81 + i / (double)(points - 1)))
            .ToList()
        ),
      ],
    };

  private sealed class CountingRegions(
    Func<RoutePoint, RouteRegion>? resolve = null
  ) : IRouteRegionLookup
  {
    public int Calls { get; private set; }

    public RouteRegion Find(RoutePoint point)
    {
      Calls++;
      return resolve?.Invoke(point) ?? new("US", "Etc/UTC", false);
    }
  }
}
