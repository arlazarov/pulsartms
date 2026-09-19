using Application.Features.Dispatch.Models;
using Application.Features.Eta.Models;
using Domain.Entities.Fleet;
using Infrastructure.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using DispatchEntity = global::Domain.Entities.Dispatch.Dispatch;

namespace Server.Tests.Dispatch;

[Trait("Category", "Dispatch")]
[Trait("Kind", "Integration")]
public sealed class DispatchEtaOwnershipTests
{
  [Theory]
  [InlineData("101", "102")]
  [InlineData("102", "101")]
  public async Task ConflictingHeaderNumberCannotInheritTheAssignedStopsForecast(
    string ownerNumber,
    string otherNumber
  )
  {
    await using var connection = new SqliteConnection("Data Source=:memory:");
    await connection.OpenAsync();
    await using var db = new AppDbContext(
      new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connection).Options
    );
    await db.Database.EnsureCreatedAsync();
    var owner = new Truck
    {
      Id = Guid.NewGuid(),
      ExternalId = "owner",
      UnitNumber = ownerNumber,
      IsActive = true,
    };
    var other = new Truck
    {
      Id = Guid.NewGuid(),
      ExternalId = "other",
      UnitNumber = otherNumber,
      IsActive = true,
    };
    var day = DateOnly.FromDateTime(DateTime.UtcNow);
    var load = new DispatchEntity
    {
      Id = Guid.NewGuid(),
      LoadNumber = 100,
      Status = "assigned",
      TruckNumber = otherNumber,
      ShipDate = day,
      DeliveryDate = day,
      Stops =
      [
        new()
        {
          Id = Guid.NewGuid(),
          Sequence = 1,
          Job = "Pick Up",
          TruckId = owner.Id,
          ScheduledDate = day,
          ScheduledTime = new(10, 0),
        },
        new()
        {
          Id = Guid.NewGuid(),
          Sequence = 2,
          Job = "Drop Off",
          TruckId = owner.Id,
          ScheduledDate = day,
          ScheduledTime = new(12, 0),
        },
      ],
    };
    db.Trucks.AddRange(owner, other);
    db.Dispatches.Add(load);
    await db.SaveChangesAsync();
    using var services = new PlanningTestServices(db);
    var description = (
      await services.EtaInputs.DescribeAsync(owner.Id, default)
    )!;
    var now = DateTime.UtcNow;
    var forecast = new DispatchEta(
      now,
      now.AddMinutes(2),
      [
        new(load.Stops[0].Id, now.AddHours(1), "Etc/UTC", null, 0, 60, 0)
        {
          DispatchId = load.Id,
        },
      ],
      null,
      []
    );
    Assert.True(
      await new EtaForecastStore(db).SaveAsync(
        [
          new(
            load.Id,
            owner.Id,
            load.Id,
            description.InputHash,
            description.DriverExternalId,
            forecast
          ),
        ],
        default
      )
    );

    var board = (
      await services.Board.Handle(
        new(IncludeHos: false, IncludeFinancials: false),
        default
      )
    ).Response!;
    var assigned = Assert.Single(
      board.Items.Single(x => x.TruckId == owner.Id).Dispatches
    );
    var conflicting = Assert.Single(
      board.Items.Single(x => x.TruckId == other.Id).Dispatches
    );
    Assert.NotSame(assigned, conflicting);
    Assert.Equal(load.Stops[0].Id, Assert.Single(assigned.Eta!.Stops).StopId);
    Assert.Null(conflicting.Eta);
  }
}
