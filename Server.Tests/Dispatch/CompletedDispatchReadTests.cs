using Application.Features.Dispatch.Queries;
using Application.Features.Routing.Services.Deadheads;
using Domain.Entities.Fleet;
using Infrastructure.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Load = Domain.Entities.Dispatch.Dispatch;

namespace Server.Tests.Dispatch;

[Trait("Category", "Dispatch")]
[Trait("Kind", "Integration")]
public sealed class CompletedDispatchReadTests
{
  [Theory]
  [InlineData("Drop Off", false, true)]
  [InlineData("DELIVERY", false, true)]
  [InlineData("Drop Off", true, false)]
  [InlineData("Pick Up", false, false)]
  public async Task SourceDeliveryAppearsInHistoryUnlessReopened(
    string job,
    bool reopened,
    bool visible
  )
  {
    await using var connection = new SqliteConnection("Data Source=:memory:");
    await connection.OpenAsync();
    await using var db = new AppDbContext(
      new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connection).Options
    );
    await db.Database.EnsureCreatedAsync();
    var load = Completed(
      new() { Id = Guid.NewGuid(), ExternalId = "delivered" },
      1370,
      new(2026, 9, 12)
    );
    load.Status = "sent";
    var final = load.Stops.Last();
    final.Job = job;
    final.DeliveredAt = final.DepartedAt;
    final.DepartedAt = null;
    final.CompletionOverride = reopened ? false : null;
    db.Dispatches.Add(load);
    await db.SaveChangesAsync();
    using var services = new PlanningTestServices(db);

    var response = (
      await new GetDispatchQueryHandler(db, services.Deadheads).Handle(
        new(Status: "completed"),
        default
      )
    ).Response!;

    Assert.Equal(visible ? 1 : 0, response.TotalCount);
    Assert.All(response.Items, item => Assert.True(item.Completed));
  }

  [Fact]
  public async Task CompletedPageReturnsStopsAndSavedFinancialsWithoutProvidersOrWrites()
  {
    await using var connection = new SqliteConnection("Data Source=:memory:");
    await connection.OpenAsync();
    await using var db = new AppDbContext(
      new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connection).Options
    );
    await db.Database.EnsureCreatedAsync();
    var truck = new Truck
    {
      Id = Guid.NewGuid(),
      ExternalId = "archive",
      UnitNumber = "54777",
    };
    var previous = Completed(truck, 100, new(2026, 8, 1));
    var current = Completed(truck, 200, new(2026, 8, 3));
    current.LoadedMiles = 250;
    current.Price = 1000;
    var active = Completed(truck, 300, new(2026, 8, 5));
    active.Status = "assigned";
    active.Stops.Last().DepartedAt = null;
    db.Dispatches.AddRange(previous, current, active);
    await db.SaveChangesAsync();
    using var services = new PlanningTestServices(db);
    var profile = await services.Routes.ProfileAsync(truck.Id, default);
    var pair = Assert.IsType<DeadheadConnection>(
      DeadheadConnection.Find(current, [previous])
    );
    db.DispatchDeadheads.Add(
      new()
      {
        Id = Guid.NewGuid(),
        DispatchId = current.Id,
        PreviousDispatchId = previous.Id,
        InputHash = pair.Signature(profile),
        Miles = 50,
      }
    );
    await db.SaveChangesAsync();
    db.ChangeTracker.Clear();
    var handler = new GetDispatchQueryHandler(db, services.Deadheads);

    var page = (
      await handler.Handle(
        new(
          PageSize: 1,
          Status: "completed",
          TruckId: truck.Id,
          Search: "Historic"
        ),
        default
      )
    ).Response!;

    Assert.Equal(2, page.TotalCount);
    var result = Assert.Single(page.Items);
    Assert.Equal(current.Id, result.Id);
    Assert.Equal(2, result.Stops.Count);
    Assert.Equal(
      new[] { "Pick Up", "Drop Off" },
      result.Stops.Select(stop => stop.Job)
    );
    Assert.All(result.Stops, stop => Assert.NotNull(stop.DepartedAt));
    Assert.Equal("Historic Driver", result.DriverName);
    Assert.Equal(1000m, result.Price);
    Assert.Equal("USD", result.Currency);
    Assert.Equal(250m, result.LoadedMiles);
    Assert.Equal(50m, result.EmptyMiles);
    Assert.Equal(300m, result.TotalMiles);
    Assert.Equal(4m, result.LoadedRatePerMile);
    Assert.Equal(3.333333m, result.TotalRatePerMile);
    Assert.Null(result.Eta);
    Assert.DoesNotContain(
      db.ChangeTracker.Entries(),
      entry => entry.State is not EntityState.Unchanged
    );

    var second = (
      await handler.Handle(
        new(Page: 2, PageSize: 1, Status: "completed", TruckId: truck.Id),
        default
      )
    ).Response!;
    Assert.Equal(previous.Id, Assert.Single(second.Items).Id);
    var otherTruck = (
      await handler.Handle(
        new(Status: "completed", TruckId: Guid.NewGuid()),
        default
      )
    ).Response!;
    Assert.Empty(otherTruck.Items);
    var trailer = (
      await handler.Handle(
        new(Status: "completed", Search: "HISTORY-1"),
        default
      )
    ).Response!;
    Assert.Equal(2, trailer.TotalCount);
    Assert.All(
      trailer.Items,
      load => Assert.Equal("HISTORY-1", load.TrailerNumber)
    );
    var activeTrailer = (
      await handler.Handle(
        new(Status: "assigned", Search: "HISTORY-1"),
        default
      )
    ).Response!;
    Assert.Empty(activeTrailer.Items);
  }

  [Fact]
  public async Task ExistingActiveListKeepsThinResponseAndDoesNotEnrichFinancials()
  {
    await using var connection = new SqliteConnection("Data Source=:memory:");
    await connection.OpenAsync();
    await using var db = new AppDbContext(
      new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connection).Options
    );
    await db.Database.EnsureCreatedAsync();
    var load = Completed(
      new()
      {
        Id = Guid.NewGuid(),
        ExternalId = "active",
        UnitNumber = "11005",
      },
      123,
      new(2026, 8, 1)
    );
    load.Status = "assigned";
    db.Dispatches.Add(load);
    await db.SaveChangesAsync();
    using var services = new PlanningTestServices(db);
    var response = (
      await new GetDispatchQueryHandler(db, services.Deadheads).Handle(
        new(Status: "assigned"),
        default
      )
    ).Response!;
    var row = Assert.Single(response.Items);
    Assert.Empty(row.Stops);
    Assert.Null(row.LoadedMiles);
    Assert.Null(row.LoadedRatePerMile);
    Assert.Null(row.Eta);
  }

  private static Load Completed(Truck truck, int number, DateOnly day) =>
    new()
    {
      Id = Guid.NewGuid(),
      Truck = truck,
      LoadNumber = number,
      Status = "completed",
      TruckNumber = truck.UnitNumber,
      DriverName = "Historic Driver",
      TrailerNumber = "HISTORY-1",
      CustomerName = "Historic Customer",
      ShipDate = day,
      DeliveryDate = day.AddDays(1),
      Price = 500,
      LoadedMiles = 100,
      Currency = "USD",
      Stops =
      [
        new()
        {
          Id = Guid.NewGuid(),
          Sequence = 1,
          Job = "Pick Up",
          ScheduledDate = day,
          City = "Origin",
          DepartedAt = day.ToDateTime(new TimeOnly(10, 0), DateTimeKind.Utc),
        },
        new()
        {
          Id = Guid.NewGuid(),
          Sequence = 2,
          Job = "Drop Off",
          ScheduledDate = day.AddDays(1),
          City = "Destination",
          DepartedAt = day.AddDays(1)
            .ToDateTime(new TimeOnly(10, 0), DateTimeKind.Utc),
        },
      ],
    };
}
