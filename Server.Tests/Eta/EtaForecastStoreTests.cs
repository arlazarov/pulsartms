using System.Text.Json.Nodes;
using Domain.Entities.Dispatch;
using Domain.Entities.Execution;
using Domain.Entities.Fleet;
using Domain.Models.Eta;
using Infrastructure.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Load = Domain.Entities.Dispatch.Dispatch;

namespace Server.Tests.Eta;

[Trait("Category", "Eta")]
[Trait("Kind", "Integration")]
public sealed class EtaForecastStoreTests
{
  private static readonly DateTime Now = new(
    2026,
    9,
    8,
    18,
    0,
    0,
    DateTimeKind.Utc
  );

  [Fact]
  public async Task ForecastSurvivesANewContextAndPreservesStopIdentityAndLocalTimes()
  {
    await using var fixture = await Fixture.CreateAsync();
    var snapshot = fixture.Snapshot(fixture.Current.Id, Now);
    Assert.True(
      await new EtaForecastStore(
        fixture.Db,
        NullLogger<EtaForecastStore>.Instance
      ).SaveAsync([snapshot], default)
    );
    await using var another = new AppDbContext(fixture.Options);
    var saved = Assert.Single(
      await new EtaForecastStore(
        another,
        NullLogger<EtaForecastStore>.Instance
      ).ReadAsync([fixture.Current.Id], default)
    );
    Assert.Equal(snapshot.DispatchId, saved.DispatchId);
    Assert.Equal(snapshot.TruckId, saved.TruckId);
    Assert.Equal(snapshot.RootDispatchId, saved.RootDispatchId);
    Assert.Equal(snapshot.DriverExternalId, saved.DriverExternalId);
    Assert.Equal(snapshot.InputHash, saved.InputHash);
    Assert.Equal(snapshot.Forecast.CalculatedAt, saved.Forecast.CalculatedAt);
    Assert.Equal(snapshot.Forecast.ValidUntil, saved.Forecast.ValidUntil);
    Assert.Equal(
      snapshot.Forecast.CycleAtCalculation,
      saved.Forecast.CycleAtCalculation
    );
    var expectedStop = Assert.Single(snapshot.Forecast.Stops);
    var actualStop = Assert.Single(saved.Forecast.Stops);
    Assert.Equal(
      expectedStop with
      {
        Hours = null,
      },
      actualStop with
      {
        Hours = null,
      }
    );
    Assert.Equal(
      expectedStop.Hours! with
      {
        Alternatives = [],
      },
      actualStop.Hours! with
      {
        Alternatives = [],
      }
    );
    Assert.Equal(
      expectedStop.Hours.Alternatives,
      actualStop.Hours.Alternatives
    );
    Assert.Empty(
      await new EtaForecastStore(
        another,
        NullLogger<EtaForecastStore>.Instance
      ).ReadAsync([Guid.NewGuid()], default)
    );
  }

  [Fact]
  public async Task SnapshotIdentitySurvivesDatabaseTimestampPrecision()
  {
    await using var fixture = await Fixture.CreateAsync();
    var store = new EtaForecastStore(
      fixture.Db,
      NullLogger<EtaForecastStore>.Instance
    );
    Assert.True(
      await store.SaveAsync(
        [fixture.Snapshot(fixture.Current.Id, Now.AddTicks(7))],
        default
      )
    );
    var row = await fixture
      .Db.Set<DispatchEtaForecast>()
      .AsNoTracking()
      .SingleAsync();
    var snapshot = Assert.Single(
      await store.ReadAsync([fixture.Current.Id], default)
    );
    Assert.Equal(Now, row.CalculatedAt);
    Assert.Equal(Now.AddTicks(7), snapshot.Forecast.CalculatedAt);
    Assert.Equal(row.ValidUntil.AddTicks(7), snapshot.Forecast.ValidUntil);
    Assert.Equal(0, row.ValidUntil.Ticks % 10);
  }

  [Fact]
  public async Task OlderSavedForecastWithoutCycleFieldsDoesNotInventHours()
  {
    await using var fixture = await Fixture.CreateAsync();
    var store = new EtaForecastStore(
      fixture.Db,
      NullLogger<EtaForecastStore>.Instance
    );
    Assert.True(
      await store.SaveAsync(
        [fixture.Snapshot(fixture.Current.Id, Now)],
        default
      )
    );
    var row = await fixture.Db.Set<DispatchEtaForecast>().SingleAsync();
    var json = JsonNode.Parse(row.ForecastJson)!;
    json.AsObject().Remove("cycleAtCalculation");
    foreach (var stop in json["stops"]!.AsArray())
    {
      stop!.AsObject().Remove("cycleAfterDeparture");
      stop.AsObject().Remove("hours");
    }
    row.ForecastJson = json.ToJsonString();
    await fixture.Db.SaveChangesAsync();
    await using var another = new AppDbContext(fixture.Options);
    var saved = Assert.Single(
      await new EtaForecastStore(
        another,
        NullLogger<EtaForecastStore>.Instance
      ).ReadAsync([fixture.Current.Id], default)
    );
    Assert.Null(Assert.Single(saved.Forecast.Stops).CycleAfterDeparture);
    Assert.Null(saved.Forecast.CycleAtCalculation);
    Assert.Null(Assert.Single(saved.Forecast.Stops).Hours);
  }

  [Fact]
  public async Task OlderBatchCannotOverwriteNewerForecastOrLeavePartialInserts()
  {
    await using var fixture = await Fixture.CreateAsync();
    var store = new EtaForecastStore(
      fixture.Db,
      NullLogger<EtaForecastStore>.Instance
    );
    Assert.True(
      await store.SaveAsync(
        [fixture.Snapshot(fixture.Current.Id, Now)],
        default
      )
    );
    Assert.False(
      await store.SaveAsync(
        [
          fixture.Snapshot(fixture.Future.Id, Now.AddMinutes(-1)),
          fixture.Snapshot(fixture.Current.Id, Now.AddMinutes(-1)),
        ],
        default
      )
    );
    Assert.Single(
      await store.ReadAsync([fixture.Current.Id, fixture.Future.Id], default)
    );
    Assert.True(
      await store.SaveAsync(
        [
          fixture.Snapshot(fixture.Future.Id, Now.AddMinutes(1)),
          fixture.Snapshot(fixture.Current.Id, Now.AddMinutes(1)),
        ],
        default
      )
    );
    Assert.Equal(
      2,
      (
        await store.ReadAsync([fixture.Current.Id, fixture.Future.Id], default)
      ).Count
    );
  }

  [Fact]
  public async Task InvalidPayloadIsUnavailableAndCancelledSaveDoesNotWrite()
  {
    await using var fixture = await Fixture.CreateAsync();
    var store = new EtaForecastStore(
      fixture.Db,
      NullLogger<EtaForecastStore>.Instance
    );
    using var cancelled = new CancellationTokenSource();
    cancelled.Cancel();
    await Assert.ThrowsAnyAsync<OperationCanceledException>(
      () =>
        store.SaveAsync(
          [fixture.Snapshot(fixture.Current.Id, Now)],
          cancelled.Token
        )
    );
    Assert.Empty(await store.ReadAsync([fixture.Current.Id], default));
    Assert.True(
      await store.SaveAsync(
        [fixture.Snapshot(fixture.Current.Id, Now)],
        default
      )
    );
    var row = await fixture.Db.Set<DispatchEtaForecast>().SingleAsync();
    row.ForecastJson = "{invalid";
    await fixture.Db.SaveChangesAsync();
    Assert.Empty(await store.ReadAsync([fixture.Current.Id], default));
  }

  [Fact]
  public async Task NativeLegForecastsCoexistWithoutReplacingLegacyOrEachOther()
  {
    await using var fixture = await Fixture.CreateAsync();
    var otherTruck = new Truck
    {
      Id = Guid.NewGuid(),
      UnitNumber = "other",
      ExternalId = "other",
    };
    var firstTrip = new Trip { Id = Guid.NewGuid() };
    var secondTrip = new Trip { Id = Guid.NewGuid() };
    var first = new ExecutionLeg
    {
      Id = Guid.NewGuid(),
      TripId = firstTrip.Id,
      Trip = firstTrip,
      TruckId = fixture.Current.TruckId!.Value,
      Revision = 1,
    };
    var second = new ExecutionLeg
    {
      Id = Guid.NewGuid(),
      TripId = secondTrip.Id,
      Trip = secondTrip,
      TruckId = otherTruck.Id,
      Revision = 1,
    };
    fixture.Db.Trucks.Add(otherTruck);
    fixture.Db.Trips.AddRange(firstTrip, secondTrip);
    fixture.Db.ExecutionLegs.AddRange(first, second);
    await fixture.Db.SaveChangesAsync();
    var store = new EtaForecastStore(
      fixture.Db,
      NullLogger<EtaForecastStore>.Instance
    );
    var legacy = fixture.Snapshot(fixture.Current.Id, Now);
    var firstSnapshot = legacy with
    {
      ExecutionLegId = first.Id,
      RootExecutionLegId = first.Id,
      AssignmentRevision = 1,
    };
    var secondSnapshot = legacy with
    {
      TruckId = otherTruck.Id,
      ExecutionLegId = second.Id,
      RootExecutionLegId = second.Id,
      AssignmentRevision = 1,
    };

    Assert.True(
      await store.SaveAsync([legacy, firstSnapshot, secondSnapshot], default)
    );
    Assert.Null(
      Assert
        .Single(await store.ReadAsync([fixture.Current.Id], default))
        .ExecutionLegId
    );
    var native = await store.ReadExecutionLegsAsync(
      [first.Id, second.Id],
      default
    );
    Assert.Equal(2, native.Count);
    Assert.Contains(
      native,
      x => x.ExecutionLegId == first.Id && x.TruckId == first.TruckId
    );
    Assert.Contains(
      native,
      x => x.ExecutionLegId == second.Id && x.TruckId == second.TruckId
    );
  }

  [Fact]
  public async Task ChangedExecutionRevisionRejectsStaleForecastPublication()
  {
    await using var fixture = await Fixture.CreateAsync();
    var trip = new Trip { Id = Guid.NewGuid() };
    var leg = new ExecutionLeg
    {
      Id = Guid.NewGuid(),
      TripId = trip.Id,
      Trip = trip,
      TruckId = fixture.Current.TruckId!.Value,
      Revision = 2,
    };
    fixture.Db.Trips.Add(trip);
    fixture.Db.ExecutionLegs.Add(leg);
    await fixture.Db.SaveChangesAsync();
    var store = new EtaForecastStore(
      fixture.Db,
      NullLogger<EtaForecastStore>.Instance
    );
    var snapshot = fixture.Snapshot(fixture.Current.Id, Now) with
    {
      ExecutionLegId = leg.Id,
      RootExecutionLegId = leg.Id,
      AssignmentRevision = 1,
    };

    Assert.False(await store.SaveAsync([snapshot], default));
    Assert.Empty(await store.ReadExecutionLegsAsync([leg.Id], default));
    Assert.True(
      await store.SaveAsync([snapshot with { AssignmentRevision = 2 }], default)
    );
    Assert.Equal(
      2,
      Assert
        .Single(await store.ReadExecutionLegsAsync([leg.Id], default))
        .AssignmentRevision
    );
    await Assert.ThrowsAsync<InvalidOperationException>(
      () => fixture.Db.LockExecutionLegAsync(leg.Id, 2, default)
    );
  }

  private sealed class Fixture(
    SqliteConnection connection,
    AppDbContext db,
    DbContextOptions<AppDbContext> options,
    Truck truck,
    Load current,
    Load future
  ) : IAsyncDisposable
  {
    public AppDbContext Db => db;
    public DbContextOptions<AppDbContext> Options => options;
    public Load Current => current;
    public Load Future => future;

    public EtaForecastSnapshot Snapshot(Guid dispatchId, DateTime at)
    {
      var arrival = new DateTimeOffset(at)
        .ToOffset(TimeSpan.FromHours(-4))
        .AddHours(2);
      var stop = new StopEta(
        Guid.NewGuid(),
        arrival,
        "America/New_York",
        arrival.AddMinutes(-30),
        30,
        120,
        0
      )
      {
        DispatchId = dispatchId,
        ServiceStart = arrival,
        Departure = arrival.AddHours(2),
        CycleAfterDeparture = new(
          750,
          arrival.AddDays(1),
          480,
          "America/New_York",
          true
        ),
        Hours = new(
          -60,
          -180,
          60,
          arrival.AddHours(-1),
          true,
          [
            new(
              "recap",
              arrival.AddHours(8),
              arrival.AddHours(10),
              510,
              300,
              arrival.AddHours(-1),
              arrival.AddHours(7)
            ),
          ],
          null
        ),
      };
      return new(
        dispatchId,
        truck.Id,
        current.Id,
        new string('a', 64),
        "driver-test",
        new(at, at.AddMinutes(2), [stop], null, ["Estimated"])
        {
          CycleAtCalculation = new(
            990,
            arrival.AddHours(12),
            185,
            "America/New_York",
            true
          ),
        }
      );
    }

    public static async Task<Fixture> CreateAsync()
    {
      var connection = new SqliteConnection("Data Source=:memory:");
      await connection.OpenAsync();
      var options = new DbContextOptionsBuilder<AppDbContext>()
        .UseSqlite(connection)
        .Options;
      var db = new AppDbContext(options);
      await db.Database.EnsureCreatedAsync();
      var truck = new Truck { Id = Guid.NewGuid() };
      var current = new Load
      {
        Id = Guid.Parse("00000000-0000-0000-0000-000000000002"),
        LoadNumber = 1,
        TruckId = truck.Id,
      };
      var future = new Load
      {
        Id = Guid.Parse("00000000-0000-0000-0000-000000000001"),
        LoadNumber = 2,
        TruckId = truck.Id,
      };
      db.Trucks.Add(truck);
      db.Dispatches.AddRange(current, future);
      await db.SaveChangesAsync();
      return new(connection, db, options, truck, current, future);
    }

    public async ValueTask DisposeAsync()
    {
      await db.DisposeAsync();
      await connection.DisposeAsync();
    }
  }
}
