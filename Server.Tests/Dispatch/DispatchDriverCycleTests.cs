using Application.Features.Dispatch.Models;
using Domain.Entities.Fleet;
using Domain.Models.Eta;
using Infrastructure.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using DispatchEntity = global::Domain.Entities.Dispatch.Dispatch;

namespace Server.Tests.Dispatch;

[Trait("Category", "Dispatch")]
[Trait("Kind", "Integration")]
public sealed class DispatchDriverCycleTests
{
  [Theory]
  [InlineData("ready", true)]
  [InlineData("pending", true)]
  [InlineData("different-driver", false)]
  [InlineData("different-truck", false)]
  [InlineData("different-root", false)]
  [InlineData("different-inputs", false)]
  [InlineData("refresh-grace", true)]
  [InlineData("grace-boundary", false)]
  [InlineData("expired", false)]
  [InlineData("invalid-deadline", false)]
  [InlineData("future-calculation", false)]
  [InlineData("past-recap", false)]
  [InlineData("zero-recap", false)]
  [InlineData("unverified", false)]
  [InlineData("unknown", false)]
  [InlineData("future-load-only", false)]
  [InlineData("missing", false)]
  public async Task HeaderRecapUsesOnlyTheEligibleCurrentDriverBaseline(
    string scenario,
    bool expected
  )
  {
    await using var connection = new SqliteConnection("Data Source=:memory:");
    await connection.OpenAsync();
    await using var db = new AppDbContext(
      new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connection).Options
    );
    await db.Database.EnsureCreatedAsync();
    var driver = new Driver
    {
      Id = Guid.NewGuid(),
      ExternalId = "current-driver",
      Name = "Current driver",
    };
    var truck = new Truck
    {
      Id = Guid.NewGuid(),
      ExternalId = "current-truck",
      UnitNumber = "101",
      IsActive = true,
      Driver = driver,
    };
    var otherTruck = new Truck
    {
      Id = Guid.NewGuid(),
      ExternalId = "other-truck",
      UnitNumber = "102",
      IsActive = true,
    };
    var now = DateTime.UtcNow.AddSeconds(-2);
    var date = DateOnly.FromDateTime(now);
    DispatchEntity Load(int number, DateOnly day) =>
      new()
      {
        Id = Guid.NewGuid(),
        TruckId = truck.Id,
        DriverId = driver.Id,
        LoadNumber = number,
        Status = "assigned",
        ShipDate = day,
        DeliveryDate = day,
        Stops =
        [
          new()
          {
            Id = Guid.NewGuid(),
            Sequence = 1,
            Job = "Pick Up",
            TruckId = truck.Id,
            ScheduledDate = day,
          },
          new()
          {
            Id = Guid.NewGuid(),
            Sequence = 2,
            Job = "Drop Off",
            TruckId = truck.Id,
            ScheduledDate = day,
          },
        ],
      };
    var current = Load(100, date);
    var future = Load(101, date.AddDays(1));
    db.Trucks.AddRange(truck, otherTruck);
    db.Dispatches.AddRange(current, future);
    await db.SaveChangesAsync();
    using var services = new PlanningTestServices(db);
    var description = (
      await services.EtaInputs.DescribeAsync(truck.Id, default)
    )!;
    var recapAt = new DateTimeOffset(now)
      .ToOffset(TimeSpan.FromHours(-4))
      .AddDays(1);
    var baseline = new StopCycleForecast(
      600,
      recapAt,
      185,
      "America/New_York",
      true
    );
    var afterStop = baseline with
    {
      NextRecapAt = recapAt.AddDays(2),
      NextRecapMinutes = 600,
    };
    var forecast = new DispatchEta(
      now,
      now.AddMinutes(2),
      [
        new(
          current.Stops[0].Id,
          recapAt.AddDays(1),
          "Etc/UTC",
          null,
          null,
          60,
          0
        )
        {
          DispatchId = current.Id,
          CycleAfterDeparture = afterStop,
        },
      ],
      null,
      []
    )
    {
      CycleAtCalculation = baseline,
    };
    forecast = scenario switch
    {
      "pending" => forecast with { Stops = [], RouteUpdatePending = true },
      "refresh-grace" => forecast with
      {
        CalculatedAt = now.AddMinutes(-7),
        ValidUntil = now.AddMinutes(-5),
      },
      "grace-boundary" => forecast with
      {
        CalculatedAt = now.AddMinutes(-17),
        ValidUntil = now.AddMinutes(-15),
      },
      "expired" => forecast with
      {
        CalculatedAt = now.AddMinutes(-18),
        ValidUntil = now.AddMinutes(-16),
      },
      "invalid-deadline" => forecast with { ValidUntil = now },
      "future-calculation" => forecast with
      {
        CalculatedAt = now.AddDays(1),
        ValidUntil = now.AddDays(1).AddMinutes(2),
      },
      "past-recap" => forecast with
      {
        CycleAtCalculation = baseline with { NextRecapAt = now.AddSeconds(-1) },
      },
      "zero-recap" => forecast with
      {
        CycleAtCalculation = baseline with { NextRecapMinutes = 0 },
      },
      "unverified" => forecast with
      {
        CycleAtCalculation = baseline with { RecapVerified = false },
      },
      "unknown" => forecast with { CycleAtCalculation = null },
      _ => forecast,
    };
    var snapshot = new EtaForecastSnapshot(
      current.Id,
      truck.Id,
      current.Id,
      description.InputHash,
      driver.ExternalId,
      forecast
    );
    snapshot = scenario switch
    {
      "different-driver" => snapshot with
      {
        DriverExternalId = "previous-driver",
      },
      "different-truck" => snapshot with { TruckId = otherTruck.Id },
      "different-root" => snapshot with { RootDispatchId = future.Id },
      "different-inputs" => snapshot with
      {
        InputHash = "previous-route-inputs",
      },
      "future-load-only" => snapshot with { DispatchId = future.Id },
      _ => snapshot,
    };
    if (scenario != "missing")
      Assert.True(
        await new EtaForecastStore(
          db,
          NullLogger<EtaForecastStore>.Instance
        ).SaveAsync([snapshot], default)
      );

    var board = (
      await services.Board.Handle(
        new(TruckId: truck.Id, IncludeHos: false, IncludeFinancials: false),
        default
      )
    ).Response!;
    var row = Assert.Single(board.Items);

    if (expected)
    {
      var shown = Assert.IsType<DriverCycleSnapshot>(row.CurrentCycle);
      Assert.Equal(baseline, shown.Cycle);
      Assert.Equal(
        TimeSpan.FromHours(-4),
        shown.Cycle.NextRecapAt!.Value.Offset
      );
      Assert.Equal(forecast.CalculatedAt, shown.CalculatedAt);
      Assert.Equal(forecast.ValidUntil, shown.ValidUntil);
      Assert.NotEqual(afterStop, shown.Cycle);
      if (scenario == "refresh-grace")
        Assert.True(
          Assert
            .Single(row.Dispatches, x => x.Id == current.Id)
            .Eta!.RouteUpdatePending
        );
    }
    else
      Assert.Null(row.CurrentCycle);
    Assert.Empty(services.EtaMemory.Results);

    using var internalReads = new PlanningTestServices(db);
    var internalBoard = (
      await internalReads.Board.Handle(
        new(
          TruckId: truck.Id,
          IncludeHos: false,
          IncludeFinancials: false,
          IncludeEta: false
        ),
        default
      )
    ).Response!;
    Assert.Null(Assert.Single(internalBoard.Items).CurrentCycle);
    Assert.Empty(internalReads.EtaMemory.Viewed);
    Assert.Empty(internalReads.EtaMemory.Results);
  }

  [Fact]
  public async Task TruckWithoutAssignedLoadsHasNoInventedRecap()
  {
    await using var connection = new SqliteConnection("Data Source=:memory:");
    await connection.OpenAsync();
    await using var db = new AppDbContext(
      new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connection).Options
    );
    await db.Database.EnsureCreatedAsync();
    db.Trucks.Add(
      new()
      {
        Id = Guid.NewGuid(),
        ExternalId = "idle-truck",
        UnitNumber = "101",
        IsActive = true,
      }
    );
    await db.SaveChangesAsync();
    using var services = new PlanningTestServices(db);

    var board = (
      await services.Board.Handle(
        new(IncludeHos: false, IncludeFinancials: false),
        default
      )
    ).Response!;

    var row = Assert.Single(board.Items);
    Assert.Null(row.CurrentCycle);
    Assert.Empty(row.Dispatches);
    Assert.Empty(services.EtaMemory.Viewed);
  }
}
