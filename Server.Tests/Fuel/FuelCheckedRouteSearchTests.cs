using Application.Features.Routing.Interfaces;
using Application.Features.Routing.Services.FuelPlanning;
using Domain.Models.Routing;
using Domain.Rules;

namespace Server.Tests.Fuel;

[Trait("Category", "Fuel")]
[Trait("Kind", "Unit")]
public sealed class FuelCheckedRouteSearchTests
{
  [Fact]
  public async Task DirectBaselineReusesEveryLegWithoutProviderCalls()
  {
    var (baseline, stops) = Baseline();
    baseline.Seconds += .5;
    var provider = new Provider();
    var search = new FuelCheckedRouteSearch(baseline, stops, new(), provider);
    var result = await search.CheckAsync([], default);
    Assert.NotNull(result.Route);
    Assert.Equal(18000, result.Route.Seconds);
    for (var index = 0; index < baseline.Legs.Count; index++)
      Assert.Same(baseline.Legs[index], result.Route.Legs[index]);
    Assert.Empty(result.Stations);
    Assert.Empty(result.Route.Points);
    Assert.Empty(provider.Requests);
    Assert.Equal(1, search.RoadChecks);
    Assert.Equal(0, search.RetainedPoints);
  }

  [Fact]
  public async Task OnlyChangedMandatoryLegIsRoutedAndActualOffsetsUseCanonicalLegSums()
  {
    var (baseline, stops) = Baseline();
    var provider = new Provider();
    var search = new FuelCheckedRouteSearch(baseline, stops, new(), provider);
    var fuel = Fuel(1);
    var result = await search.CheckAsync([fuel], default);
    var route = Assert.IsType<TruckRoute>(result.Route);
    Assert.Equal(
      new[]
      {
        baseline.Legs[1].Points[0],
        fuel.Station.Point,
        baseline.Legs[1].Points[^1],
      },
      Assert.Single(provider.Requests)
    );
    Assert.Same(baseline.Legs[0], route.Legs[0]);
    Assert.Same(baseline.Legs[2], route.Legs[2]);
    Assert.Equal(260, route.Miles);
    Assert.Equal(15600, route.Seconds);
    Assert.Empty(route.Points);
    Assert.Equal(130, Assert.Single(result.Stations).AlongMiles);
    Assert.Equal(0, result.Stations[0].Station.DetourMinutes);
    Assert.Equal(25, fuel.Station.DetourMinutes);
    Assert.Equal(2, search.RoadChecks);
    Assert.Equal(3, search.RetainedPoints);
    Assert.Equal(100, baseline.Legs[1].Miles);
  }

  [Fact]
  public async Task CommonRecipeIsReusedWithCurrentPricesAndReturnVisitsKeepTheirOwnLeg()
  {
    var (baseline, stops) = Baseline();
    var provider = new Provider();
    var search = new FuelCheckedRouteSearch(baseline, stops, new(), provider);
    var first = Fuel(0);
    var later = Fuel(2, first.Station.StationId, first.Station.Point);
    var firstResult = await search.CheckAsync([first], default);
    var newer = first with
    {
      PriceUsd = 2.5,
      Station = new()
      {
        StationId = first.Station.StationId,
        Point = first.Station.Point,
        Name = "Current price",
        YourPrice = 2.5,
        DetourMinutes = 11,
      },
    };
    var second = await search.CheckAsync([later, newer], default);
    Assert.NotNull(firstResult.Route);
    Assert.NotNull(second.Route);
    Assert.Equal(2, provider.Requests.Count);
    Assert.Equal(new[] { 0, 2 }, second.Stations.Select(fuel => fuel.LegIndex));
    Assert.Equal(
      new[] { 30d, 190d },
      second.Stations.Select(fuel => fuel.AlongMiles)
    );
    Assert.NotEqual(second.Stations[0].VisitKey, second.Stations[1].VisitKey);
    Assert.Equal("Current price", second.Stations[0].Station.Name);
    Assert.Equal(2.5, second.Stations[0].PriceUsd);
    Assert.Equal(2.5, second.Stations[0].Station.YourPrice);
    Assert.NotSame(newer.Station, second.Stations[0].Station);
    Assert.Equal(3, search.RoadChecks);
  }

  [Fact]
  public async Task ChangedStationCoordinatesAndOrderRequireNewRecipeOnlyForThatLeg()
  {
    var (baseline, stops) = Baseline();
    var provider = new Provider();
    var search = new FuelCheckedRouteSearch(baseline, stops, new(), provider);
    var first = Fuel(1) with { AlongMiles = 125 };
    var second = Fuel(1) with { AlongMiles = 175 };
    Assert.NotNull((await search.CheckAsync([first, second], default)).Route);
    var moved = first with
    {
      Station = new()
      {
        StationId = first.Station.StationId,
        Point = new(40.1, -78.8),
      },
    };
    Assert.NotNull((await search.CheckAsync([moved, second], default)).Route);
    Assert.NotNull(
      (
        await search.CheckAsync(
          [moved with { AlongMiles = 180 }, second],
          default
        )
      ).Route
    );
    Assert.Equal(3, provider.Requests.Count);
    Assert.Equal(second.Station.Point, provider.Requests[^1][1]);
    Assert.Equal(4, search.RetainedPoints);
  }

  [Fact]
  public async Task EqualMileVisitsKeepTheirInputOrder()
  {
    var (baseline, stops) = Baseline();
    var provider = new Provider();
    var search = new FuelCheckedRouteSearch(baseline, stops, new(), provider);
    var fuels = Enumerable
      .Range(0, 18)
      .Select(index => Fuel(1, point: new(40, -79 + index / 100d)))
      .ToArray();
    var result = await search.CheckAsync(fuels, default);
    Assert.NotNull(result.Route);
    Assert.Equal(
      fuels.Select(fuel => fuel.VisitKey),
      result.Stations.Select(fuel => fuel.VisitKey)
    );
    Assert.Equal(
      fuels.Select(fuel => fuel.Station.Point),
      Assert.Single(provider.Requests).Skip(1).Take(fuels.Length)
    );
  }

  [Fact]
  public async Task ExhaustedBudgetMakesNoCallsAndNeverPublishesPartialChain()
  {
    var (baseline, stops) = Baseline();
    var provider = new Provider();
    var search = new FuelCheckedRouteSearch(
      baseline,
      stops,
      new(),
      provider,
      maximumRoadChecks: 2
    );
    var first = Fuel(0);
    var second = Fuel(2);
    Assert.NotNull((await search.CheckAsync([first], default)).Route);
    var rejected = await search.CheckAsync([first, second], default);
    Assert.True(rejected.BudgetExceeded);
    Assert.Null(rejected.Route);
    Assert.Empty(rejected.Stations);
    Assert.Single(provider.Requests);
    Assert.Equal(2, search.RoadChecks);
    Assert.NotNull((await search.CheckAsync([first], default)).Route);
    Assert.Single(provider.Requests);
  }

  [Fact]
  public async Task ManyChangedLegsUseOneCompleteRequestWhenSegmentCallsWouldExceedBudget()
  {
    var (baseline, stops) = Baseline(12);
    var provider = new Provider();
    var search = new FuelCheckedRouteSearch(baseline, stops, new(), provider);
    var fuels = Enumerable.Range(0, 12).Select(index => Fuel(index)).ToArray();
    var result = await search.CheckAsync(fuels, default);
    Assert.NotNull(result.Route);
    Assert.Null(result.RejectionReason);
    var requested = Assert.Single(provider.Requests);
    Assert.Equal(25, requested.Length);
    Assert.Equal(baseline.Legs[0].Points[0], requested[0]);
    for (var index = 0; index < stops.Length; index++)
    {
      Assert.Equal(fuels[index].Station.Point, requested[index * 2 + 1]);
      Assert.Equal(baseline.Legs[index].Points[^1], requested[index * 2 + 2]);
      Assert.Equal(index, result.Stations[index].LegIndex);
      Assert.Equal(index * 60 + 30, result.Stations[index].AlongMiles);
    }
    Assert.Equal(12, result.Route.Legs.Count);
    Assert.Equal(720, result.Route.Miles);
    Assert.Equal(43200, result.Route.Seconds);
    Assert.Empty(result.Route.Points);
    Assert.Equal(2, search.RoadChecks);
    Assert.Equal(0, search.RetainedPoints);
  }

  [Theory]
  [InlineData("anchor")]
  [InlineData("fuel")]
  [InlineData("points")]
  public async Task WholeRequestFallbackRejectsInvalidRoadAnchorsFuelHitsAndPointBounds(
    string error
  )
  {
    var (baseline, stops) = Baseline();
    var provider = new Provider
    {
      Alter = route =>
      {
        if (error == "points")
        {
          var leg = route.Legs[0];
          route.Legs[0] = leg with
          {
            Points = Enumerable
              .Repeat(leg.Points[0], 9)
              .Append(leg.Points[^1])
              .ToList(),
          };
          return;
        }
        var index = error == "anchor" ? 1 : 0;
        var point = route.Legs[index].Points[^1] with
        {
          Latitude =
            route.Legs[index].Points[^1].Latitude
            + (error == "anchor" ? .06 : .6) / 69,
        };
        route.Legs[index] = route.Legs[index] with
        {
          Points = [route.Legs[index].Points[0], point],
        };
        route.Legs[index + 1] = route.Legs[index + 1] with
        {
          Points = [point, route.Legs[index + 1].Points[^1]],
        };
      },
    };
    var search = new FuelCheckedRouteSearch(
      baseline,
      stops,
      new(),
      provider,
      maximumRoadChecks: 2,
      maximumGeometryPoints: 10
    );
    var result = await search.CheckAsync([Fuel(0), Fuel(2)], default);
    Assert.Null(result.Route);
    Assert.NotNull(result.RejectionReason);
    Assert.Single(provider.Requests);
    Assert.Equal(2, search.RoadChecks);
    Assert.Equal(0, search.RetainedPoints);
  }

  [Theory]
  [InlineData("start")]
  [InlineData("end")]
  [InlineData("fuel")]
  [InlineData("join")]
  [InlineData("miles")]
  [InlineData("seconds")]
  public async Task InvalidCheckedSegmentsAreRejectedWithoutPoisoningRecipeCache(
    string error
  )
  {
    var (baseline, stops) = Baseline();
    var provider = new Provider
    {
      Alter = route =>
      {
        var first = route.Legs[0];
        var last = route.Legs[^1];
        RoutePoint Shift(RoutePoint point, double miles) =>
          point with
          {
            Latitude = point.Latitude + miles / 69,
          };
        if (error == "start")
          route.Legs[0] = first with
          {
            Points = [Shift(first.Points[0], .06), first.Points[^1]],
          };
        if (error == "end")
          route.Legs[^1] = last with
          {
            Points = [last.Points[0], Shift(last.Points[^1], .06)],
          };
        if (error == "fuel")
        {
          var point = Shift(first.Points[^1], .6);
          route.Legs[0] = first with { Points = [first.Points[0], point] };
          route.Legs[^1] = last with { Points = [point, last.Points[^1]] };
        }
        if (error == "join")
          route.Legs[^1] = last with
          {
            Points = [Shift(last.Points[0], .06), last.Points[^1]],
          };
        if (error == "miles")
          route.Miles += 1;
        if (error == "seconds")
          route.Seconds += 100;
      },
    };
    var search = new FuelCheckedRouteSearch(baseline, stops, new(), provider);
    var fuel = Fuel(1);
    var rejected = await search.CheckAsync([fuel], default);
    Assert.Null(rejected.Route);
    Assert.NotNull(rejected.RejectionReason);
    Assert.Equal(0, search.RetainedPoints);
    provider.Alter = null;
    Assert.NotNull((await search.CheckAsync([fuel], default)).Route);
    Assert.Equal(2, provider.Requests.Count);
  }

  [Fact]
  public async Task UsesEachLegsOwnRoadAnchorAtAValidSnappedMandatoryBoundary()
  {
    var (baseline, stops) = Baseline();
    var leg = baseline.Legs[1];
    var snapped = leg.Points[0] with
    {
      Latitude = leg.Points[0].Latitude + .04 / 69,
    };
    baseline.Legs[1] = leg with { Points = [snapped, leg.Points[^1]] };
    var provider = new Provider();
    var search = new FuelCheckedRouteSearch(baseline, stops, new(), provider);
    Assert.NotNull((await search.CheckAsync([Fuel(1)], default)).Route);
    Assert.Equal(snapped, Assert.Single(provider.Requests)[0]);
    Assert.NotEqual(baseline.Legs[0].Points[^1], provider.Requests[0][0]);
  }

  [Fact]
  public async Task RecipeCacheAndAssembledCandidateBothRespectAggregatePointLimit()
  {
    var (baseline, stops) = Baseline();
    var provider = new Provider();
    var search = new FuelCheckedRouteSearch(
      baseline,
      stops,
      new(),
      provider,
      maximumGeometryPoints: 8
    );
    var fuels = Enumerable.Range(0, 3).Select(index => Fuel(index)).ToArray();
    foreach (var fuel in fuels)
    {
      Assert.NotNull((await search.CheckAsync([fuel], default)).Route);
      Assert.InRange(search.RetainedPoints, 0, 8);
    }
    Assert.Equal(6, search.RetainedPoints);
    var rejected = await search.CheckAsync(fuels, default);
    Assert.Null(rejected.Route);
    Assert.Contains("geometry limit", rejected.RejectionReason);
    Assert.InRange(search.RetainedPoints, 0, 8);
    Assert.NotNull((await search.CheckAsync([fuels[0]], default)).Route);
    Assert.InRange(search.RetainedPoints, 0, 8);
  }

  [Fact]
  public async Task InvalidVisitOwnershipAndWaypointBoundsDoNotCallProvider()
  {
    var (baseline, stops) = Baseline();
    var provider = new Provider();
    var search = new FuelCheckedRouteSearch(baseline, stops, new(), provider);
    var invalid = new[]
    {
      Fuel(1) with
      {
        LegIndex = -1,
      },
      Fuel(1) with
      {
        AlongMiles = 10,
      },
      Fuel(1) with
      {
        LegIndex = 3,
      },
    };
    foreach (var fuel in invalid)
      Assert.Null((await search.CheckAsync([fuel], default)).Route);
    var duplicate = Fuel(1);
    Assert.Null(
      (await search.CheckAsync([duplicate, duplicate], default)).Route
    );
    Assert.Null(
      (
        await search.CheckAsync(
          Enumerable.Range(0, 47).Select(_ => Fuel(1)).ToArray(),
          default
        )
      ).Route
    );
    Assert.Empty(provider.Requests);
  }

  [Fact]
  public async Task CancelledProviderResponseCannotPublishOrCacheGeometry()
  {
    var (baseline, stops) = Baseline();
    using var cancellation = new CancellationTokenSource();
    var provider = new Provider { Alter = _ => cancellation.Cancel() };
    var search = new FuelCheckedRouteSearch(baseline, stops, new(), provider);
    await Assert.ThrowsAnyAsync<OperationCanceledException>(
      () => search.CheckAsync([Fuel(0)], cancellation.Token)
    );
    Assert.Equal(2, search.RoadChecks);
    Assert.Equal(0, search.RetainedPoints);
  }

  [Fact]
  public void InvalidBaselineFailsBeforeAnyCandidateCalls()
  {
    var (baseline, stops) = Baseline();
    baseline.Legs[^1] = baseline.Legs[^1] with
    {
      Points = [baseline.Legs[^1].Points[0], new(45, -70)],
    };
    var provider = new Provider();
    Assert.Throws<RoutePlanningException>(
      () => new FuelCheckedRouteSearch(baseline, stops, new(), provider)
    );
    Assert.Empty(provider.Requests);
  }

  [Fact]
  public void EmptyMileageBaselineCannotReachCommit()
  {
    var (baseline, stops) = Baseline();
    baseline.Miles = 0;
    baseline.Legs = baseline
      .Legs.Select(leg => leg with { Miles = 0 })
      .ToList();
    Assert.Throws<RoutePlanningException>(
      () => new FuelCheckedRouteSearch(baseline, stops, new(), new Provider())
    );
  }

  private static (TruckRoute Route, PlanStop[] Stops) Baseline(int count = 3)
  {
    var points = Enumerable
      .Range(0, count + 1)
      .Select(index => new RoutePoint(40, -80 + index % 3))
      .ToArray();
    var route = new TruckRoute
    {
      Miles = count * 100,
      Seconds = count * 6000,
      CalculatedAt = DateTime.UnixEpoch,
      Legs = points
        .Zip(points.Skip(1), (from, to) => new RouteLeg(100, 6000, [from, to]))
        .ToList(),
    };
    var stops = points
      .Skip(1)
      .Select(
        (point, index) =>
          new PlanStop(Guid.NewGuid(), "Stop", "", index + 1, point)
      )
      .ToArray();
    return (route, stops);
  }

  private static FuelCandidate Fuel(
    int leg,
    Guid? id = null,
    RoutePoint? point = null
  ) =>
    new(
      new()
      {
        StationId = id ?? Guid.NewGuid(),
        Point = point ?? new(40, -79.5 + leg / 2d),
        Name = "Fuel",
        DetourMinutes = 25,
      },
      leg * 100 + 50,
      1,
      1,
      3,
      3
    )
    {
      LegIndex = leg,
      EntryMiles = leg * 100 + 40,
      ExitMiles = leg * 100 + 60,
    };

  private sealed class Provider : IRoutingProvider
  {
    public bool IsConfigured => true;
    public List<RoutePoint[]> Requests { get; } = [];
    public Action<TruckRoute>? Alter { get; set; }

    public Task<RoutePoint> GeocodeAsync(
      string address,
      CancellationToken ct
    ) => throw new NotSupportedException();

    public Task<TruckRoute> CalculateAsync(
      IReadOnlyList<RoutePoint> points,
      TruckRouteProfile profile,
      CancellationToken ct
    )
    {
      Requests.Add(points.ToArray());
      var route = new TruckRoute
      {
        Miles = (points.Count - 1) * 30,
        Seconds = (points.Count - 1) * 1800 + .5,
        Legs = points
          .Zip(points.Skip(1), (from, to) => new RouteLeg(30, 1800, [from, to]))
          .ToList(),
      };
      Alter?.Invoke(route);
      return Task.FromResult(route);
    }
  }
}
