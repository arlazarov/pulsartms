using System.Data.Common;
using Application.Features.Routing.Models;
using Domain.Entities.Fleet;
using Infrastructure.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Load = Domain.Entities.Dispatch.Dispatch;

namespace Server.Tests.Support;

public sealed class TruckFuelPlanFixture(
  SqliteConnection connection,
  AppDbContext db,
  DbContextOptions<AppDbContext> options,
  Truck truck,
  Load current,
  Load future,
  FuelStorageCommands commands
) : IAsyncDisposable
{
  public static readonly DateTime Now = new(
    2026,
    9,
    8,
    18,
    0,
    0,
    DateTimeKind.Utc
  );
  public AppDbContext Db => db;
  public DbContextOptions<AppDbContext> Options => options;
  public Guid TruckId => truck.Id;
  public Guid CurrentId => current.Id;
  public Guid FutureId => future.Id;
  public FuelStorageCommands Commands => commands;

  public TruckFuelPlanSnapshot Snapshot(DateTime? at = null)
  {
    var timestamp = at ?? Now;
    var first = new PlanStop(
      Guid.NewGuid(),
      "Delivery",
      "Current address",
      1,
      new(43, -79)
    );
    var second = new PlanStop(
      Guid.NewGuid(),
      "Pickup",
      "Future address",
      1,
      new(44, -80)
    );
    var stationId = Guid.NewGuid();
    return new(
      truck.Id,
      current.Id,
      timestamp,
      new FuelPlan
      {
        TruckId = truck.Id,
        CalculatedAt = timestamp,
        DispatchIds = [current.Id, future.Id],
        RemainingMiles = 200,
        AssignmentSignature = "assignments",
        ProfileSignature = "profile",
        StartingGallons = 100,
        Stops =
        [
          new()
          {
            StationId = stationId,
            VisitKey = $"{stationId:N}:1",
            DispatchId = future.Id,
            BeforeStopId = second.Id,
            Point = new(43.5, -79.5),
            MilesAhead = 140,
            Name = "Fuel",
            BuyGallons = 40,
            PriceDate = DateOnly.FromDateTime(timestamp),
          },
        ],
      },
      [new(current.Id, first, 100), new(future.Id, second, 200)],
      new TruckRoute
      {
        CalculatedAt = timestamp,
        Miles = 200,
        Seconds = 10000,
        Legs =
        [
          new(100, 5000, [new(42, -78), first.Point]),
          new(100, 5000, [first.Point, second.Point]),
        ],
        Points = [new(42, -78), first.Point, second.Point],
      }
    );
  }

  public static async Task<TruckFuelPlanFixture> CreateAsync()
  {
    var connection = new SqliteConnection("Data Source=:memory:");
    await connection.OpenAsync();
    var commands = new FuelStorageCommands();
    var options = new DbContextOptionsBuilder<AppDbContext>()
      .UseSqlite(connection)
      .AddInterceptors(commands)
      .Options;
    var db = new AppDbContext(options);
    await db.Database.EnsureCreatedAsync();
    var truck = new Truck { Id = Guid.NewGuid() };
    var current = new Load
    {
      Id = Guid.NewGuid(),
      LoadNumber = 1,
      TruckId = truck.Id,
    };
    var future = new Load
    {
      Id = Guid.NewGuid(),
      LoadNumber = 2,
      TruckId = truck.Id,
    };
    db.Trucks.Add(truck);
    db.Dispatches.AddRange(current, future);
    await db.SaveChangesAsync();
    commands.Reads.Clear();
    return new(connection, db, options, truck, current, future, commands);
  }

  public async ValueTask DisposeAsync()
  {
    await db.DisposeAsync();
    await connection.DisposeAsync();
  }
}

public sealed class FuelStorageCommands : DbCommandInterceptor
{
  public List<string> Reads { get; } = [];
  public Func<Task>? BeforeWrite { get; set; }

  public override ValueTask<
    InterceptionResult<DbDataReader>
  > ReaderExecutingAsync(
    DbCommand command,
    CommandEventData eventData,
    InterceptionResult<DbDataReader> result,
    CancellationToken cancellationToken = default
  )
  {
    if (
      command.CommandText.Contains("TruckFuelPlans", StringComparison.Ordinal)
    )
      Reads.Add(command.CommandText);
    return ValueTask.FromResult(result);
  }

  public override async ValueTask<
    InterceptionResult<int>
  > NonQueryExecutingAsync(
    DbCommand command,
    CommandEventData eventData,
    InterceptionResult<int> result,
    CancellationToken cancellationToken = default
  )
  {
    if (
      (
        command.CommandText.Contains(
          "INSERT INTO \"TruckFuelPlans\"",
          StringComparison.Ordinal
        )
        || command.CommandText.Contains(
          "UPDATE \"TruckFuelPlans\"",
          StringComparison.Ordinal
        )
      ) && BeforeWrite is { } before
    )
    {
      BeforeWrite = null;
      await before();
    }
    return result;
  }
}
