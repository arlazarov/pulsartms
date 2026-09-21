using System.Text.Json;
using Application.Features.Execution.Services;
using Application.Features.Mileage.Models;
using Application.Features.Mileage.Services;
using Application.Features.Routing.Services.Routes;
using Domain.Entities.Dispatch;
using Domain.Entities.Execution;
using Domain.Entities.Fleet;
using Domain.Entities.Mileage;
using Domain.Models.Execution;
using Domain.Models.Routing;
using Domain.Rules;
using Domain.Rules.Routing;
using Microsoft.EntityFrameworkCore;

namespace Server.Tests.Mileage;

[Trait("Category", "Dispatch")]
[Trait("Kind", "Integration")]
public sealed class AutomaticMileageRecorderTests
{
  [Theory]
  [InlineData(false)]
  [InlineData(true)]
  public async Task CursorReplayCannotCreateDuplicateObservedWork(
    bool reversedSourceSequence
  )
  {
    await using var fixture = await StopCompletionFixture.CreateAsync();
    var (leg, stops, recorder) = await SeedAsync(fixture);
    if (reversedSourceSequence)
    {
      stops[0].Sequence = 20;
      stops[1].Sequence = 10;
      ExecutionStopRows.Replace(leg, stops);
      await fixture.Db.SaveChangesAsync();
    }
    var at = fixture.Clock.GetUtcNow().AddHours(-2);
    var first = new OdometerPage(
      [new("odometer-truck", at, 100_000m)],
      "a",
      false
    );
    Assert.True(await recorder.CaptureOdometerAsync(null, first, default));
    Assert.False(await recorder.CaptureOdometerAsync(null, first, default));
    Assert.Equal(0, await fixture.Db.Movements.CountAsync());
    var second = new OdometerPage(
      [new("odometer-truck", at.AddHours(1), 116_093.44m)],
      "b",
      false
    );
    Assert.True(await recorder.CaptureOdometerAsync("a", second, default));
    Assert.Equal(0, await fixture.Db.Movements.CountAsync());
    Assert.Equal(1, await fixture.Db.OdometerIntervals.CountAsync());
    stops[1].ArrivedAt = at.AddHours(1).UtcDateTime;
    ExecutionStopRows.Replace(leg, stops);
    await fixture.Db.SaveChangesAsync();
    Assert.True(
      await recorder.CaptureOdometerAsync("b", new([], "b", false), default)
    );
    Assert.True(
      await recorder.CaptureOdometerAsync("b", new([], "b", false), default)
    );
    var movement = await fixture.Db.Movements.SingleAsync();
    Assert.Equal("samsara-obd", movement.Origin);
    Assert.Null(movement.PlannedMiles);
    Assert.Equal(10m, movement.ActualMiles);
    Assert.Equal(leg.TruckId, movement.TruckId);
    Assert.Equal(leg.DriverId, movement.DriverId);
    Assert.Equal(fixture.Load.Id, movement.AllocatedDispatchId);
    var evidence = await fixture.Db.MovementDistanceEvidence.SingleAsync();
    Assert.Equal(100_000m, evidence.StartOdometerMeters);
    Assert.Equal(116_093.44m, evidence.EndOdometerMeters);
    Assert.Equal(at.UtcDateTime, evidence.StartedAt);
    Assert.Equal(at.AddHours(1).UtcDateTime, evidence.EndedAt);
  }

  [Fact]
  public async Task ManualOverlapOnlyExcludesTheIntersectingRawPair()
  {
    await using var fixture = await StopCompletionFixture.CreateAsync();
    var (leg, stops, recorder) = await SeedAsync(fixture);
    var at = fixture.Clock.GetUtcNow().AddHours(-2);
    stops[1].ArrivedAt = at.AddHours(1).UtcDateTime;
    ExecutionStopRows.Replace(leg, stops);
    fixture.Db.Movements.Add(
      new Movement
      {
        Id = Guid.NewGuid(),
        IdempotencyKey = Guid.NewGuid(),
        TruckId = leg.TruckId,
        ExecutionLegId = leg.Id,
        StartedAt = at.AddMinutes(25).UtcDateTime,
        EndedAt = at.AddMinutes(35).UtcDateTime,
        ActualMiles = 5m,
        RecordedAt = fixture.Clock.GetUtcNow().UtcDateTime,
        Revision = 1,
      }
    );
    await fixture.Db.SaveChangesAsync();
    var page = new OdometerPage(
      [
        new("odometer-truck", at, 100_000m),
        new("odometer-truck", at.AddMinutes(20), 116_093.44m),
        new("odometer-truck", at.AddMinutes(40), 132_186.88m),
        new("odometer-truck", at.AddHours(1), 148_280.32m),
      ],
      "captured",
      false
    );
    Assert.True(await recorder.CaptureOdometerAsync(null, page, default));
    var observed = await fixture
      .Db.Movements.Where(x => x.Origin == "samsara-obd")
      .OrderBy(x => x.StartedAt)
      .ToListAsync();
    Assert.Equal(2, observed.Count);
    Assert.All(observed, movement => Assert.Equal(10m, movement.ActualMiles));
    Assert.Equal(at.UtcDateTime, observed[0].StartedAt);
    Assert.Equal(at.AddMinutes(20).UtcDateTime, observed[0].EndedAt);
    Assert.Equal(at.AddMinutes(40).UtcDateTime, observed[1].StartedAt);
    Assert.Equal(at.AddHours(1).UtcDateTime, observed[1].EndedAt);
    var gap = await fixture.Db.MileageCaptureGaps.SingleAsync(x =>
      x.Reason == "existing-physical-movement"
    );
    Assert.Equal(at.AddMinutes(20).UtcDateTime, gap.StartedAt);
    Assert.Equal(at.AddMinutes(40).UtcDateTime, gap.EndedAt);
    Assert.Equal(
      2,
      await fixture.Db.OdometerIntervals.CountAsync(x => x.Status == "recorded")
    );
    Assert.Equal(
      1,
      await fixture.Db.OdometerIntervals.CountAsync(x => x.Status == "gap")
    );
    Assert.True(
      await recorder.CaptureOdometerAsync(
        "captured",
        new([], "captured", false),
        default
      )
    );
    var allMiles = await fixture
      .Db.Movements.Select(x => x.ActualMiles)
      .ToListAsync();
    Assert.Equal(3, allMiles.Count);
    Assert.Equal(25m, allMiles.Sum());
    Assert.Equal(2, await fixture.Db.MovementDistanceEvidence.CountAsync());
  }

  [Theory]
  [InlineData(false)]
  [InlineData(true)]
  public async Task PlannedRecorderVersionsSavedRoadWithoutCreatingActuals(
    bool reversedSourceSequence
  )
  {
    await using var fixture = await StopCompletionFixture.CreateAsync();
    var (leg, stops, recorder) = await SeedAsync(fixture);
    if (reversedSourceSequence)
    {
      stops[0].Sequence = 20;
      stops[1].Sequence = 10;
      ExecutionStopRows.Replace(leg, stops);
      await fixture.Db.SaveChangesAsync();
    }
    Assert.False(await recorder.CapturePlannedAsync(leg.Id, default));
    var profiles = new TruckPlanningProfileService(
      fixture.Db,
      fixture.Reads,
      fixture.Planning.Settings,
      fixture.Planning.ExchangeRates
    );
    var profile = await profiles.GetAsync(leg.TruckId, default);
    var points = stops
      .Select(x => new RoutePoint((double)x.Latitude!, (double)x.Longitude!))
      .ToList();
    var road = new TruckRoute
    {
      Miles = 100,
      Seconds = 6000,
      Legs = [new(100, 6000, points)],
      Points = points,
    };
    var saved = new DispatchBaseRoute
    {
      Id = Guid.NewGuid(),
      DispatchId = fixture.Load.Id,
      ExecutionLegId = leg.Id,
      CalculatedAt = fixture.Clock.GetUtcNow().UtcDateTime,
      InputHash = BaseRouteService.Signature(
        RouteWorkProjection.Capture(
          fixture.Load,
          leg,
          ExecutionStopRows.Read(leg)
        ),
        profile
      ),
      RouteJson = JsonSerializer.Serialize(road, RoutingJson.Options),
    };
    fixture.Db.DispatchBaseRoutes.Add(saved);
    await fixture.Db.SaveChangesAsync();
    var reread = await fixture
      .Db.ExecutionLegs.AsNoTracking()
      .SingleAsync(x => x.Id == leg.Id);
    Assert.Equal(
      ExecutionSnapshots.Write(ExecutionStopRows.Read(leg)),
      ExecutionSnapshots.Write(ExecutionStopRows.Read(reread))
    );
    Assert.True(await recorder.CapturePlannedAsync(leg.Id, default));
    Assert.True(await recorder.CapturePlannedAsync(leg.Id, default));
    var movement = await fixture.Db.Movements.SingleAsync();
    Assert.Equal(100m, movement.PlannedMiles);
    Assert.Null(movement.ActualMiles);
    Assert.Null(movement.StartedAt);
    Assert.Equal(1, await fixture.Db.MovementDistanceEvidence.CountAsync());
    road.Miles = 101;
    road.Legs = [new(101, 6000, points)];
    saved.RouteJson = JsonSerializer.Serialize(road, RoutingJson.Options);
    await fixture.Db.SaveChangesAsync();
    Assert.True(await recorder.CapturePlannedAsync(leg.Id, default));
    Assert.Equal(1, await fixture.Db.Movements.CountAsync());
    Assert.Equal(2, await fixture.Db.MovementDistanceEvidence.CountAsync());
    Assert.Equal(101m, movement.PlannedMiles);
  }

  private static async Task<(
    ExecutionLeg,
    DispatchStop[],
    AutomaticMileageRecorder
  )> SeedAsync(StopCompletionFixture fixture)
  {
    var at = fixture.Clock.GetUtcNow().AddHours(-2).UtcDateTime;
    var truck = new Truck
    {
      Id = Guid.NewGuid(),
      ExternalId = "odometer-truck",
    };
    var trailer = new Trailer { Id = Guid.NewGuid() };
    var trip = new Trip { Id = Guid.NewGuid(), RecordedAt = at.AddDays(-1) };
    var stops = new DispatchStop[]
    {
      new()
      {
        Id = Guid.NewGuid(),
        DispatchId = fixture.Load.Id,
        Sequence = 1,
        Job = "Pick Up",
        StateAfter = "Loaded",
        DepartedAt = at,
        Address = "Origin",
        Latitude = 35m,
        Longitude = -80m,
      },
      new()
      {
        Id = Guid.NewGuid(),
        DispatchId = fixture.Load.Id,
        Sequence = 2,
        Job = "Drop Off",
        Address = "Destination",
        Latitude = 36m,
        Longitude = -80m,
      },
    };
    var leg = new ExecutionLeg
    {
      Id = Guid.NewGuid(),
      TripId = trip.Id,
      TruckId = truck.Id,
      TrailerId = trailer.Id,
      Status = "active",
      Revision = 1,
      RecordedAt = at.AddDays(-1),
      StartedAt = at,
      Stops = ExecutionStopRows.Capture(stops),
      Loads =
      [
        new()
        {
          Id = Guid.NewGuid(),
          DispatchId = fixture.Load.Id,
          StartVisitId = stops[0].Id,
          EndVisitId = stops[1].Id,
        },
      ],
    };
    fixture.Db.Trucks.Add(truck);
    fixture.Db.Trailers.Add(trailer);
    fixture.Db.Trips.Add(trip);
    fixture.Db.ExecutionLegs.Add(leg);
    await fixture.Db.SaveChangesAsync();
    return (
      leg,
      stops,
      new(
        fixture.Db,
        new(
          fixture.Db,
          fixture.Reads,
          fixture.Planning.Settings,
          fixture.Planning.ExchangeRates
        ),
        fixture.Clock
      )
    );
  }
}
