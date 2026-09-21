using System.Text.Json.Nodes;
using Application.Features.Routing.Services.FuelPlanning;
using Domain.Entities.Fuel;
using Domain.Models.Routing;
using Domain.Rules.Routing;
using Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Server.Tests.Routing;

[Trait("Category", "Routing")]
[Trait("Kind", "Integration")]
public sealed class TruckFuelPlanStoreTests
{
  [Fact]
  public async Task EstimatedAccessRoundTripsBaselineGeometryAndSeparateOccurrenceMileage()
  {
    await using var fixture = await TruckFuelPlanFixture.CreateAsync();
    var expected = EstimatedSnapshot(fixture.Snapshot());
    Assert.True(
      await new TruckFuelPlanStore(fixture.Db).SaveAsync(expected, default)
    );
    await using var another = new AppDbContext(fixture.Options);
    var store = new TruckFuelPlanStore(another);
    fixture.Commands.Reads.Clear();
    var summary = Assert.IsType<TruckFuelPlanSnapshot>(
      await store.ReadAsync(fixture.TruckId, false, default)
    );
    Assert.DoesNotContain(
      "CheckedRouteJson",
      Assert.Single(fixture.Commands.Reads),
      StringComparison.Ordinal
    );
    Assert.True(summary.Plan.EstimatedStationAccess);
    Assert.Null(summary.CheckedRoute);
    Assert.Null(summary.BaselineRoute);
    Assert.Equal(198, summary.Plan.Stops[0].RouteMilesAhead);
    Assert.Equal(204, summary.Plan.Stops[0].MilesAhead);
    Assert.Equal(212, summary.Plan.RemainingMiles);
    Assert.Equal(12, summary.Plan.Stops[0].DetourMiles);
    Assert.Equal(18, summary.Plan.Stops[0].DetourMinutes);
    Assert.Equal(expected.Stops, summary.Stops);

    var complete = Assert.IsType<TruckFuelPlanSnapshot>(
      await store.ReadAsync(fixture.TruckId, true, default)
    );
    Assert.Null(complete.CheckedRoute);
    Assert.Equal(200, complete.BaselineRoute!.Miles);
    Assert.Equal(
      expected.BaselineRoute!.Legs.SelectMany(x => x.Points),
      complete.BaselineRoute.Legs.SelectMany(x => x.Points)
    );
    Assert.Empty(complete.BaselineRoute.Points);
    var row = await another.Set<TruckFuelPlan>().AsNoTracking().SingleAsync();
    var envelope = JsonNode.Parse(row.CheckedRouteJson!)!;
    Assert.Null(envelope["checked"]);
    Assert.NotNull(envelope["baseline"]);
  }

  [Theory]
  [InlineData("missing-occurrence")]
  [InlineData("nan-occurrence")]
  [InlineData("infinite-occurrence")]
  [InlineData("negative-occurrence")]
  [InlineData("negative-origin-occurrence")]
  [InlineData("before-owned-leg")]
  [InlineData("after-owned-leg")]
  [InlineData("occurrence-order")]
  [InlineData("negative-access-miles")]
  [InlineData("nan-access-miles")]
  [InlineData("infinite-access-miles")]
  [InlineData("negative-access-minutes")]
  [InlineData("nan-access-minutes")]
  [InlineData("infinite-access-minutes")]
  [InlineData("nan-arrival-miles")]
  public async Task InvalidEstimatedOccurrenceOrAccessCannotReplaceTheSavedSnapshot(
    string corruption
  )
  {
    await using var fixture = await TruckFuelPlanFixture.CreateAsync();
    var store = new TruckFuelPlanStore(fixture.Db);
    var original = EstimatedSnapshot(fixture.Snapshot());
    Assert.True(await store.SaveAsync(original, default));
    var invalid = EstimatedSnapshot(
      fixture.Snapshot(TruckFuelPlanFixture.Now.AddMinutes(1))
    );
    var purchase = invalid.Plan.Stops[0];
    switch (corruption)
    {
      case "missing-occurrence":
        purchase.RouteMilesAhead = null;
        break;
      case "nan-occurrence":
        purchase.RouteMilesAhead = double.NaN;
        break;
      case "infinite-occurrence":
        purchase.RouteMilesAhead = double.PositiveInfinity;
        break;
      case "negative-occurrence":
        purchase.RouteMilesAhead = -1;
        break;
      case "negative-origin-occurrence":
        purchase.RouteMilesAhead = -.001;
        purchase.DispatchId = fixture.CurrentId;
        purchase.BeforeStopId = invalid.Stops[0].Stop.Id;
        break;
      case "before-owned-leg":
        purchase.RouteMilesAhead = 99.98;
        break;
      case "after-owned-leg":
        purchase.RouteMilesAhead = 200.02;
        break;
      case "occurrence-order":
        invalid.Plan.Stops.Add(
          new()
          {
            StationId = Guid.NewGuid(),
            Point = purchase.Point,
            VisitKey = "earlier-occurrence",
            DispatchId = purchase.DispatchId,
            BeforeStopId = purchase.BeforeStopId,
            RouteMilesAhead = 190,
            MilesAhead = 206,
            BuyGallons = 20,
          }
        );
        break;
      case "negative-access-miles":
        purchase.DetourMiles = -1;
        break;
      case "nan-access-miles":
        purchase.DetourMiles = double.NaN;
        break;
      case "infinite-access-miles":
        purchase.DetourMiles = double.PositiveInfinity;
        break;
      case "negative-access-minutes":
        purchase.DetourMinutes = -1;
        break;
      case "nan-access-minutes":
        purchase.DetourMinutes = double.NaN;
        break;
      case "infinite-access-minutes":
        purchase.DetourMinutes = double.PositiveInfinity;
        break;
      case "nan-arrival-miles":
        purchase.MilesAhead = double.NaN;
        break;
    }
    await Assert.ThrowsAsync<ArgumentException>(
      () => store.SaveAsync(invalid, default)
    );
    var saved = Assert.IsType<TruckFuelPlanSnapshot>(
      await store.ReadAsync(fixture.TruckId, true, default)
    );
    Assert.Equal(original.CalculatedAt, saved.CalculatedAt);
    Assert.Equal(original.Plan.Stops[0].VisitKey, saved.Plan.Stops[0].VisitKey);
    Assert.Equal(198, saved.Plan.Stops[0].RouteMilesAhead);
  }

  [Theory]
  [InlineData("missing-baseline")]
  [InlineData("checked-road")]
  [InlineData("end-miles")]
  [InlineData("mandatory-point")]
  [InlineData("continuity")]
  [InlineData("time-sum")]
  [InlineData("mile-sum")]
  public async Task EstimatedBaselineRetainsStrictGeometryAndItineraryValidation(
    string corruption
  )
  {
    await using var fixture = await TruckFuelPlanFixture.CreateAsync();
    var snapshot = EstimatedSnapshot(fixture.Snapshot());
    switch (corruption)
    {
      case "missing-baseline":
        snapshot = snapshot with { BaselineRoute = null };
        break;
      case "checked-road":
        snapshot = snapshot with { CheckedRoute = snapshot.BaselineRoute };
        break;
      case "end-miles":
        snapshot = snapshot with
        {
          Stops =
          [
            snapshot.Stops[0] with
            {
              EndMiles = 100.02,
            },
            snapshot.Stops[1],
          ],
        };
        break;
      case "mandatory-point":
        snapshot.BaselineRoute!.Legs[1].Points[^1] = new(45, -80);
        break;
      case "continuity":
        snapshot.BaselineRoute!.Legs[1].Points[0] = new(43.002, -79);
        break;
      case "time-sum":
        snapshot.BaselineRoute!.Seconds++;
        break;
      case "mile-sum":
        snapshot.BaselineRoute!.Miles++;
        break;
    }
    var store = new TruckFuelPlanStore(fixture.Db);
    await Assert.ThrowsAsync<ArgumentException>(
      () => store.SaveAsync(snapshot, default)
    );
    Assert.Null(await store.ReadAsync(fixture.TruckId, false, default));
  }

  [Theory]
  [InlineData("missing-envelope")]
  [InlineData("missing-baseline")]
  [InlineData("checked-road")]
  [InlineData("end-miles")]
  [InlineData("mandatory-point")]
  [InlineData("continuity")]
  public async Task CorruptEstimatedGeometryCannotBeLoadedAsACompleteSnapshot(
    string corruption
  )
  {
    await using var fixture = await TruckFuelPlanFixture.CreateAsync();
    var store = new TruckFuelPlanStore(fixture.Db);
    Assert.True(
      await store.SaveAsync(EstimatedSnapshot(fixture.Snapshot()), default)
    );
    var row = await fixture.Db.Set<TruckFuelPlan>().SingleAsync();
    var envelope = JsonNode.Parse(row.CheckedRouteJson!)!;
    var baseline = envelope["baseline"]!;
    switch (corruption)
    {
      case "missing-envelope":
        row.CheckedRouteJson = null;
        break;
      case "missing-baseline":
        envelope["baseline"] = null;
        break;
      case "checked-road":
        envelope["checked"] = baseline.DeepClone();
        break;
      case "end-miles":
        baseline["legs"]![0]!["miles"] = 101;
        baseline["miles"] = 201;
        break;
      case "mandatory-point":
        baseline["legs"]![1]!["points"]![1]!["latitude"] = 45;
        break;
      case "continuity":
        baseline["legs"]![1]!["points"]![0]!["latitude"] = 43.002;
        break;
    }
    if (corruption != "missing-envelope")
      row.CheckedRouteJson = envelope.ToJsonString();
    await fixture.Db.SaveChangesAsync();
    Assert.NotNull(await store.ReadAsync(fixture.TruckId, false, default));
    Assert.Null(await store.ReadAsync(fixture.TruckId, true, default));
  }

  [Theory]
  [InlineData("missing-occurrence")]
  [InlineData("out-of-leg-occurrence")]
  [InlineData("negative-access-miles")]
  [InlineData("negative-access-minutes")]
  public async Task CorruptEstimatedSummaryCannotReachProjection(
    string corruption
  )
  {
    await using var fixture = await TruckFuelPlanFixture.CreateAsync();
    var store = new TruckFuelPlanStore(fixture.Db);
    Assert.True(
      await store.SaveAsync(EstimatedSnapshot(fixture.Snapshot()), default)
    );
    var row = await fixture.Db.Set<TruckFuelPlan>().SingleAsync();
    var summary = JsonNode.Parse(row.SummaryJson)!;
    var purchase = summary["plan"]!["stops"]![0]!;
    switch (corruption)
    {
      case "missing-occurrence":
        purchase.AsObject().Remove("routeMilesAhead");
        break;
      case "out-of-leg-occurrence":
        purchase["routeMilesAhead"] = 90;
        break;
      case "negative-access-miles":
        purchase["detourMiles"] = -1;
        break;
      case "negative-access-minutes":
        purchase["detourMinutes"] = -1;
        break;
    }
    row.SummaryJson = summary.ToJsonString();
    await fixture.Db.SaveChangesAsync();
    Assert.Null(await store.ReadAsync(fixture.TruckId, false, default));
    Assert.Null(await store.ReadAsync(fixture.TruckId, true, default));
  }

  [Fact]
  public async Task LegacySnapshotWithoutEstimatedFieldsStillUsesItsCheckedGeometry()
  {
    await using var fixture = await TruckFuelPlanFixture.CreateAsync();
    var store = new TruckFuelPlanStore(fixture.Db);
    Assert.True(
      await store.SaveAsync(WithBaseline(fixture.Snapshot()), default)
    );
    var row = await fixture.Db.Set<TruckFuelPlan>().SingleAsync();
    var summary = JsonNode.Parse(row.SummaryJson)!;
    summary["plan"]!.AsObject().Remove("estimatedStationAccess");
    summary["plan"]!["stops"]![0]!.AsObject().Remove("routeMilesAhead");
    row.SummaryJson = summary.ToJsonString();
    await fixture.Db.SaveChangesAsync();
    var saved = Assert.IsType<TruckFuelPlanSnapshot>(
      await store.ReadAsync(fixture.TruckId, true, default)
    );
    Assert.False(saved.Plan.EstimatedStationAccess);
    Assert.Null(saved.Plan.Stops[0].RouteMilesAhead);
    Assert.Equal(140, saved.Plan.Stops[0].MilesAhead);
    Assert.Equal(200, saved.CheckedRoute!.Miles);
    Assert.Equal(180, saved.BaselineRoute!.Miles);
  }

  [Theory]
  [InlineData(4)]
  [InlineData(40)]
  public async Task AllAssignedDispatchesRoundTripWithinTheMandatoryStopLimit(
    int dispatchCount
  )
  {
    await using var fixture = await TruckFuelPlanFixture.CreateAsync();
    var expected = AssignedSnapshot(fixture.Snapshot(), dispatchCount);
    Assert.True(
      await new TruckFuelPlanStore(fixture.Db).SaveAsync(expected, default)
    );
    await using var another = new AppDbContext(fixture.Options);
    var store = new TruckFuelPlanStore(another);
    var summary = Assert.IsType<TruckFuelPlanSnapshot>(
      await store.ReadAsync(fixture.TruckId, false, default)
    );
    var complete = Assert.IsType<TruckFuelPlanSnapshot>(
      await store.ReadAsync(fixture.TruckId, true, default)
    );
    Assert.Equal(expected.Plan.DispatchIds, summary.Plan.DispatchIds);
    Assert.Equal(expected.Stops, summary.Stops);
    Assert.Null(summary.CheckedRoute);
    Assert.Null(summary.BaselineRoute);
    Assert.Equal(dispatchCount, complete.CheckedRoute!.Legs.Count);
    Assert.Equal(dispatchCount, complete.BaselineRoute!.Legs.Count);
    Assert.Equal(
      expected.CheckedRoute!.Legs.SelectMany(x => x.Points),
      complete.CheckedRoute.Legs.SelectMany(x => x.Points)
    );
    Assert.Equal(
      expected.Plan.DispatchIds[^1],
      Assert.Single(complete.Plan.Stops).DispatchId
    );
    Assert.Equal(
      expected.Stops[^1].Stop.Id,
      complete.Plan.Stops[0].BeforeStopId
    );
  }

  [Fact]
  public async Task FortyOneAssignedStopsCannotReplaceACompleteSavedPlan()
  {
    await using var fixture = await TruckFuelPlanFixture.CreateAsync();
    var store = new TruckFuelPlanStore(fixture.Db);
    var original = fixture.Snapshot();
    Assert.True(await store.SaveAsync(original, default));
    var oversized = AssignedSnapshot(
      fixture.Snapshot(TruckFuelPlanFixture.Now.AddMinutes(1)),
      41
    );
    await Assert.ThrowsAsync<ArgumentException>(
      () => store.SaveAsync(oversized, default)
    );
    var saved = Assert.IsType<TruckFuelPlanSnapshot>(
      await store.ReadAsync(fixture.TruckId, true, default)
    );
    Assert.Equal(original.CalculatedAt, saved.CalculatedAt);
    Assert.Equal(original.Plan.DispatchIds, saved.Plan.DispatchIds);
    Assert.Equal(original.Stops, saved.Stops);
  }

  [Fact]
  public async Task SavedSnapshotSurvivesNewContextWithExactLegGeometryAndDispatchAnchors()
  {
    await using var fixture = await TruckFuelPlanFixture.CreateAsync();
    var expected = fixture.Snapshot(TruckFuelPlanFixture.Now.AddTicks(7));
    Assert.True(
      await new TruckFuelPlanStore(fixture.Db).SaveAsync(expected, default)
    );
    await using var another = new AppDbContext(fixture.Options);
    var actual = Assert.IsType<TruckFuelPlanSnapshot>(
      await new TruckFuelPlanStore(another).ReadAsync(
        fixture.TruckId,
        true,
        default
      )
    );
    Assert.Equal(expected.CalculatedAt, actual.CalculatedAt);
    Assert.Equal(expected.RootDispatchId, actual.RootDispatchId);
    Assert.Equal(expected.Plan.DispatchIds, actual.Plan.DispatchIds);
    Assert.Equal(expected.Stops, actual.Stops);
    Assert.Equal(
      expected.Plan.Stops[0].StationId,
      actual.Plan.Stops[0].StationId
    );
    Assert.Equal(
      expected.CheckedRoute!.Legs.SelectMany(x => x.Points),
      actual.CheckedRoute!.Legs.SelectMany(x => x.Points)
    );
    Assert.Empty(actual.CheckedRoute.Points);
    var row = await another.Set<TruckFuelPlan>().AsNoTracking().SingleAsync();
    Assert.Equal(TruckFuelPlanFixture.Now, row.CalculatedAt);
  }

  [Fact]
  public async Task OrdinaryReadNeverSelectsCheckedRouteColumn()
  {
    await using var fixture = await TruckFuelPlanFixture.CreateAsync();
    var store = new TruckFuelPlanStore(fixture.Db);
    var snapshot = WithBaseline(fixture.Snapshot());
    Assert.True(await store.SaveAsync(snapshot, default));
    fixture.Commands.Reads.Clear();
    var summary = Assert.IsType<TruckFuelPlanSnapshot>(
      await store.ReadAsync(fixture.TruckId, false, default)
    );
    Assert.Null(summary.CheckedRoute);
    Assert.Null(summary.BaselineRoute);
    Assert.DoesNotContain(
      "CheckedRouteJson",
      Assert.Single(fixture.Commands.Reads),
      StringComparison.Ordinal
    );
    Assert.Equal(2, summary.Stops.Count);
    Assert.Null(await store.ReadAsync(Guid.NewGuid(), false, default));
  }

  [Fact]
  public async Task BaselineAndWinnerShareStopsButKeepIndependentMileageAndTiming()
  {
    await using var fixture = await TruckFuelPlanFixture.CreateAsync();
    var snapshot = WithBaseline(fixture.Snapshot());
    var store = new TruckFuelPlanStore(fixture.Db);
    Assert.True(await store.SaveAsync(snapshot, default));
    var actual = Assert.IsType<TruckFuelPlanSnapshot>(
      await store.ReadAsync(fixture.TruckId, true, default)
    );
    Assert.Equal(200, actual.CheckedRoute!.Miles);
    Assert.Equal(180, actual.BaselineRoute!.Miles);
    Assert.Equal(9000, actual.BaselineRoute.Seconds);
    Assert.Equal(
      snapshot.BaselineRoute!.Legs.SelectMany(x => x.Points),
      actual.BaselineRoute.Legs.SelectMany(x => x.Points)
    );
    Assert.Empty(actual.BaselineRoute.Points);
    var row = await fixture
      .Db.Set<TruckFuelPlan>()
      .AsNoTracking()
      .SingleAsync();
    Assert.Null(JsonNode.Parse(row.SummaryJson)!["baselineRoute"]);
    Assert.NotNull(JsonNode.Parse(row.CheckedRouteJson!)!["baseline"]);
  }

  [Fact]
  public async Task RoundedProviderLegsCollapseAndJoinIntoACompleteDurableSnapshot()
  {
    await using var fixture = await TruckFuelPlanFixture.CreateAsync();
    var snapshot = fixture.Snapshot();
    var first = snapshot.Stops[0].Stop;
    var second = snapshot.Stops[1].Stop;
    RoutePoint[] points =
    [
      new(42, -78),
      new(42.3, -78.3),
      new(42.6, -78.6),
      first.Point,
      new(43.5, -79.5),
      second.Point,
    ];
    var durations = new[] { 10000, 15000, 20000, 16000, 18393 };
    var raw = new TruckRoute
    {
      Miles = 200,
      Seconds = 79392,
      Points = [.. points],
      Legs = durations
        .Select(
          (seconds, i) => new RouteLeg(40, seconds, [points[i], points[i + 1]])
        )
        .ToList(),
    };
    FuelCandidate Station(int point, int leg) =>
      new(
        new() { StationId = Guid.NewGuid(), Point = points[point] },
        point * 40,
        0,
        0,
        3,
        3
      )
      {
        LegIndex = leg,
      };
    FuelRouteWaypoint[] waypoints =
    [
      new(points[0]),
      new(points[1], Fuel: Station(1, 0)),
      new(points[2], Fuel: Station(2, 0)),
      new(points[3], Stop: first),
      new(points[4], Fuel: Station(4, 1)),
      new(points[5], Stop: second),
    ];
    var collapsed = FuelRouteVariant.Collapse(raw, waypoints);
    var baseline = FuelHorizonRoad.Join(
      new()
      {
        Miles = 90,
        Seconds = 4499,
        Points = [points[0], first.Point],
        Legs = [new(90, 4500, [points[0], first.Point])],
      },
      new()
      {
        Miles = 90,
        Seconds = 4500,
        Points = [first.Point, second.Point],
        Legs = [new(90, 4500, [first.Point, second.Point])],
      }
    );
    snapshot.Plan.Stops = collapsed
      .Stations.Select(station => new FuelPlanStop
      {
        StationId = station.Station.StationId,
        Point = station.Station.Point,
        VisitKey = station.VisitKey,
        DispatchId = snapshot.Stops[station.LegIndex].DispatchId,
        BeforeStopId = snapshot.Stops[station.LegIndex].Stop.Id,
        MilesAhead = station.AlongMiles,
        BuyGallons = 10,
      })
      .ToList();
    snapshot = snapshot with
    {
      CheckedRoute = collapsed.Route,
      BaselineRoute = baseline,
      Stops =
      [
        snapshot.Stops[0] with
        {
          EndMiles = 120,
        },
        snapshot.Stops[1] with
        {
          EndMiles = 200,
        },
      ],
    };

    var store = new TruckFuelPlanStore(fixture.Db);
    Assert.True(await store.SaveAsync(snapshot, default));
    var saved = Assert.IsType<TruckFuelPlanSnapshot>(
      await store.ReadAsync(fixture.TruckId, true, default)
    );
    Assert.Equal(79393, saved.CheckedRoute!.Seconds);
    Assert.Equal(
      saved.CheckedRoute.Legs.Sum(leg => leg.Seconds),
      saved.CheckedRoute.Seconds
    );
    Assert.Equal(9000, saved.BaselineRoute!.Seconds);
    Assert.Equal(79392, raw.Seconds);
    Assert.Equal(200, saved.CheckedRoute.Miles);
    Assert.Equal(3, saved.Plan.Stops.Count);
  }

  [Fact]
  public async Task StorageStillRejectsAnUncanonicalTimingTotalEvenWithinProviderRounding()
  {
    await using var fixture = await TruckFuelPlanFixture.CreateAsync();
    var snapshot = fixture.Snapshot();
    snapshot.CheckedRoute!.Seconds++;
    await Assert.ThrowsAsync<ArgumentException>(
      () => new TruckFuelPlanStore(fixture.Db).SaveAsync(snapshot, default)
    );
    Assert.Null(
      await new TruckFuelPlanStore(fixture.Db).ReadAsync(
        fixture.TruckId,
        false,
        default
      )
    );
  }

  [Fact]
  public async Task BaselineWithDifferentMandatoryEndpointIsRejected()
  {
    await using var fixture = await TruckFuelPlanFixture.CreateAsync();
    var snapshot = WithBaseline(fixture.Snapshot());
    snapshot.BaselineRoute!.Legs[0].Points[^1] = new(45, -80);
    await Assert.ThrowsAsync<ArgumentException>(
      () => new TruckFuelPlanStore(fixture.Db).SaveAsync(snapshot, default)
    );
  }

  [Fact]
  public async Task CombinedBaselineAndWinnerPayloadUsesOneGeometryBudget()
  {
    await using var fixture = await TruckFuelPlanFixture.CreateAsync();
    var snapshot = WithBaseline(fixture.Snapshot());
    snapshot.CheckedRoute!.Warnings = [new string('x', 4 * 1024 * 1024)];
    snapshot.BaselineRoute!.Warnings = [new string('y', 4 * 1024 * 1024)];
    var store = new TruckFuelPlanStore(fixture.Db);
    await Assert.ThrowsAsync<ArgumentException>(
      () => store.SaveAsync(snapshot, default)
    );
    Assert.Null(await store.ReadAsync(fixture.TruckId, true, default));
  }

  [Theory]
  [InlineData("truck")]
  [InlineData("dispatch-order")]
  [InlineData("purchase-owner")]
  [InlineData("purchase-interval")]
  [InlineData("visit-identity")]
  [InlineData("purchase-order")]
  public async Task InconsistentTruckAndPurchaseAnchorsCannotBeStored(
    string corruption
  )
  {
    await using var fixture = await TruckFuelPlanFixture.CreateAsync();
    var snapshot = fixture.Snapshot();
    switch (corruption)
    {
      case "truck":
        snapshot.Plan.TruckId = Guid.NewGuid();
        break;
      case "dispatch-order":
        snapshot = snapshot with
        {
          Stops =
          [
            snapshot.Stops[0] with
            {
              DispatchId = fixture.FutureId,
            },
            snapshot.Stops[1] with
            {
              DispatchId = fixture.CurrentId,
            },
          ],
        };
        break;
      case "purchase-owner":
        snapshot.Plan.Stops[0].DispatchId = fixture.CurrentId;
        break;
      case "purchase-interval":
        snapshot.Plan.Stops[0].MilesAhead = 20;
        break;
      case "visit-identity":
        snapshot.Plan.Stops[0].VisitKey = "";
        break;
      case "purchase-order":
        snapshot.Plan.Stops.Insert(
          0,
          new()
          {
            StationId = Guid.NewGuid(),
            Point = snapshot.Plan.Stops[0].Point,
            DispatchId = fixture.FutureId,
            BeforeStopId = snapshot.Stops[1].Stop.Id,
            VisitKey = "later-visit",
            MilesAhead = 160,
            BuyGallons = 20,
          }
        );
        break;
    }
    var store = new TruckFuelPlanStore(fixture.Db);
    await Assert.ThrowsAsync<ArgumentException>(
      () => store.SaveAsync(snapshot, default)
    );
    Assert.Null(await store.ReadAsync(fixture.TruckId, false, default));
  }

  [Theory]
  [InlineData("time-sum")]
  [InlineData("continuity")]
  [InlineData("mandatory-point")]
  public async Task CorruptTimingOrMandatoryGeometryCannotBeReplayed(
    string corruption
  )
  {
    await using var fixture = await TruckFuelPlanFixture.CreateAsync();
    var store = new TruckFuelPlanStore(fixture.Db);
    Assert.True(await store.SaveAsync(fixture.Snapshot(), default));
    var row = await fixture.Db.Set<TruckFuelPlan>().SingleAsync();
    var geometry = JsonNode.Parse(row.CheckedRouteJson!)!;
    var route = geometry["checked"]!;
    if (corruption == "time-sum")
      route["seconds"] = 123;
    if (corruption == "continuity")
      route["legs"]![1]!["points"]![0]!["latitude"] = 45;
    if (corruption == "mandatory-point")
      route["legs"]![1]!["points"]![1]!["latitude"] = 45;
    row.CheckedRouteJson = geometry.ToJsonString();
    await fixture.Db.SaveChangesAsync();
    Assert.NotNull(await store.ReadAsync(fixture.TruckId, false, default));
    Assert.Null(await store.ReadAsync(fixture.TruckId, true, default));
  }

  [Fact]
  public async Task OnlyNewerSnapshotCanReplaceTheEntireSavedResult()
  {
    await using var fixture = await TruckFuelPlanFixture.CreateAsync();
    var store = new TruckFuelPlanStore(fixture.Db);
    var original = fixture.Snapshot();
    Assert.True(await store.SaveAsync(original, default));
    Assert.False(
      await store.SaveAsync(
        fixture.Snapshot(TruckFuelPlanFixture.Now.AddMinutes(-1)),
        default
      )
    );
    Assert.False(await store.SaveAsync(fixture.Snapshot(), default));
    var newer = fixture.Snapshot(TruckFuelPlanFixture.Now.AddMinutes(1));
    Assert.True(await store.SaveAsync(newer, default));
    var actual = Assert.IsType<TruckFuelPlanSnapshot>(
      await store.ReadAsync(fixture.TruckId, true, default)
    );
    Assert.Equal(newer.CalculatedAt, actual.CalculatedAt);
    Assert.Equal(newer.Plan.Stops[0].StationId, actual.Plan.Stops[0].StationId);
  }

  [Fact]
  public async Task StaleWriterPausedBeforeItsStatementCannotOverwriteAnInterleavedNewerSave()
  {
    await using var fixture = await TruckFuelPlanFixture.CreateAsync();
    await using var newerContext = new AppDbContext(fixture.Options);
    var older = fixture.Snapshot();
    var newer = fixture.Snapshot(TruckFuelPlanFixture.Now.AddMinutes(1));
    fixture.Commands.BeforeWrite = async () =>
      Assert.True(
        await new TruckFuelPlanStore(newerContext).SaveAsync(newer, default)
      );
    Assert.False(
      await new TruckFuelPlanStore(fixture.Db).SaveAsync(older, default)
    );
    var actual = Assert.IsType<TruckFuelPlanSnapshot>(
      await new TruckFuelPlanStore(fixture.Db).ReadAsync(
        fixture.TruckId,
        true,
        default
      )
    );
    Assert.Equal(newer.CalculatedAt, actual.CalculatedAt);
    Assert.Equal(newer.Stops, actual.Stops);
  }

  [Theory]
  [InlineData("{invalid")]
  [InlineData("null")]
  [InlineData("{}")]
  public async Task CorruptSummaryFailsClosed(string invalid)
  {
    await using var fixture = await TruckFuelPlanFixture.CreateAsync();
    var store = new TruckFuelPlanStore(fixture.Db);
    Assert.True(await store.SaveAsync(fixture.Snapshot(), default));
    var row = await fixture.Db.Set<TruckFuelPlan>().SingleAsync();
    row.SummaryJson = invalid;
    await fixture.Db.SaveChangesAsync();
    Assert.Null(await store.ReadAsync(fixture.TruckId, false, default));
    Assert.Null(await store.ReadAsync(fixture.TruckId, true, default));
  }

  [Fact]
  public async Task CorruptRouteFailsClosedOnlyWhenGeometryIsRequested()
  {
    await using var fixture = await TruckFuelPlanFixture.CreateAsync();
    var store = new TruckFuelPlanStore(fixture.Db);
    Assert.True(await store.SaveAsync(fixture.Snapshot(), default));
    var row = await fixture.Db.Set<TruckFuelPlan>().SingleAsync();
    row.CheckedRouteJson = "{invalid";
    await fixture.Db.SaveChangesAsync();
    Assert.NotNull(await store.ReadAsync(fixture.TruckId, false, default));
    Assert.Null(await store.ReadAsync(fixture.TruckId, true, default));
  }

  [Fact]
  public async Task MissingDispatchSignatureMapFailsClosedBeforeLifecycleProjection()
  {
    await using var fixture = await TruckFuelPlanFixture.CreateAsync();
    var store = new TruckFuelPlanStore(fixture.Db);
    Assert.True(await store.SaveAsync(fixture.Snapshot(), default));
    var row = await fixture.Db.Set<TruckFuelPlan>().SingleAsync();
    var summary = JsonNode.Parse(row.SummaryJson)!;
    summary["plan"]!["dispatchSignatures"] = null;
    row.SummaryJson = summary.ToJsonString();
    await fixture.Db.SaveChangesAsync();
    Assert.Null(await store.ReadAsync(fixture.TruckId, false, default));
  }

  [Fact]
  public async Task MismatchedIdentityOrEmbeddedGeometryInSummaryFailsClosed()
  {
    await using var fixture = await TruckFuelPlanFixture.CreateAsync();
    var store = new TruckFuelPlanStore(fixture.Db);
    Assert.True(await store.SaveAsync(fixture.Snapshot(), default));
    var row = await fixture.Db.Set<TruckFuelPlan>().SingleAsync();
    var summary = JsonNode.Parse(row.SummaryJson)!;
    summary["truckId"] = Guid.NewGuid().ToString();
    row.SummaryJson = summary.ToJsonString();
    await fixture.Db.SaveChangesAsync();
    Assert.Null(await store.ReadAsync(fixture.TruckId, false, default));
    summary["truckId"] = fixture.TruckId.ToString();
    summary["checkedRoute"] = JsonNode.Parse(row.CheckedRouteJson!);
    row.SummaryJson = summary.ToJsonString();
    await fixture.Db.SaveChangesAsync();
    Assert.Null(await store.ReadAsync(fixture.TruckId, false, default));
  }

  [Fact]
  public async Task OversizedItineraryOrPayloadAndCancellationDoNotWrite()
  {
    await using var fixture = await TruckFuelPlanFixture.CreateAsync();
    var store = new TruckFuelPlanStore(fixture.Db);
    var snapshot = fixture.Snapshot();
    var tooMany = snapshot with
    {
      Stops = Enumerable
        .Range(0, 41)
        .Select(i =>
          snapshot.Stops[0] with
          {
            Stop = snapshot.Stops[0].Stop with { Id = Guid.NewGuid() },
            EndMiles = i,
          }
        )
        .ToArray(),
    };
    await Assert.ThrowsAsync<ArgumentException>(
      () => store.SaveAsync(tooMany, default)
    );
    snapshot.Plan.Notes = [new string('x', 512 * 1024)];
    await Assert.ThrowsAsync<ArgumentException>(
      () => store.SaveAsync(snapshot, default)
    );
    using var cancelled = new CancellationTokenSource();
    cancelled.Cancel();
    await Assert.ThrowsAnyAsync<OperationCanceledException>(
      () => store.SaveAsync(fixture.Snapshot(), cancelled.Token)
    );
    Assert.Null(await store.ReadAsync(fixture.TruckId, false, default));
  }

  private static TruckFuelPlanSnapshot EstimatedSnapshot(
    TruckFuelPlanSnapshot snapshot
  )
  {
    snapshot.Plan.EstimatedStationAccess = true;
    snapshot.Plan.RemainingMiles = 212;
    var purchase = snapshot.Plan.Stops[0];
    purchase.RouteMilesAhead = 198;
    purchase.MilesAhead = 204;
    purchase.DetourMiles = 12;
    purchase.DetourMinutes = 18;
    return snapshot with
    {
      BaselineRoute = snapshot.CheckedRoute,
      CheckedRoute = null,
    };
  }

  private static TruckFuelPlanSnapshot AssignedSnapshot(
    TruckFuelPlanSnapshot snapshot,
    int dispatchCount
  )
  {
    var ids = snapshot
      .Plan.DispatchIds.Concat(
        Enumerable.Range(2, dispatchCount - 2).Select(_ => Guid.NewGuid())
      )
      .ToList();
    var stops = new List<FuelItineraryStop>();
    var legs = new List<RouteLeg>();
    var from = new RoutePoint(42, -80);
    for (var i = 0; i < dispatchCount; i++)
    {
      var point = new RoutePoint(42 + (i + 1) * .01, -80);
      var stop = new PlanStop(
        Guid.NewGuid(),
        "Assigned stop",
        "Address",
        1,
        point
      );
      stops.Add(new(ids[i], stop, (i + 1) * 10));
      legs.Add(new(10, 600, [from, point]));
      from = point;
    }
    snapshot.Plan.DispatchIds = ids;
    snapshot.Plan.RemainingMiles = dispatchCount * 10;
    snapshot.Plan.Stops =
    [
      new()
      {
        StationId = Guid.NewGuid(),
        VisitKey = "last-dispatch-visit",
        DispatchId = ids[^1],
        BeforeStopId = stops[^1].Stop.Id,
        Point = stops[^1].Stop.Point,
        MilesAhead = dispatchCount * 10 - 5,
        BuyGallons = 40,
      },
    ];
    var route = new TruckRoute
    {
      CalculatedAt = snapshot.CalculatedAt,
      Miles = dispatchCount * 10,
      Seconds = dispatchCount * 600,
      Legs = legs,
    };
    return snapshot with
    {
      Stops = stops,
      CheckedRoute = route,
      BaselineRoute = new TruckRoute
      {
        CalculatedAt = route.CalculatedAt,
        Miles = route.Miles,
        Seconds = route.Seconds,
        Legs = [.. legs],
      },
    };
  }

  private static TruckFuelPlanSnapshot WithBaseline(
    TruckFuelPlanSnapshot snapshot
  ) =>
    snapshot with
    {
      BaselineRoute = new TruckRoute
      {
        CalculatedAt = snapshot.CalculatedAt,
        Miles = 180,
        Seconds = 9000,
        Legs = snapshot
          .CheckedRoute!.Legs.Select(x => new RouteLeg(90, 4500, [.. x.Points]))
          .ToList(),
      },
    };
}
