using Application.Caching;
using Application.Features.Dispatch.Queries;
using Application.Features.Synchronization.Options;
using Domain.Entities.Dispatch;
using Domain.Entities.Fleet;
using Infrastructure.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Server.Tests.Dispatch;

using Dispatch = global::Domain.Entities.Dispatch.Dispatch;

[Trait("Category", "Dispatch")]
[Trait("Kind", "Integration")]
public class DispatchBoardTests
{
  [Theory]
  [InlineData("sent", true, false)]
  [InlineData("completed", false, false)]
  [InlineData("cancelled", false, false)]
  [InlineData("canceled", false, false)]
  [InlineData("sent", false, true)]
  public async Task SourceReviewRetainsOnlyUnfinishedWork(
    string status,
    bool delivered,
    bool visible
  )
  {
    await using var connection = new SqliteConnection("Data Source=:memory:");
    await connection.OpenAsync();
    await using var db = new AppDbContext(
      new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connection).Options
    );
    await db.Database.EnsureCreatedAsync();
    var date = new DateOnly(2026, 9, 21);
    var load = new Dispatch
    {
      Id = Guid.NewGuid(),
      LoadNumber = 1370,
      Status = status,
      DeliveryDate = date.AddDays(-9),
      Stops =
      [
        new()
        {
          Id = Guid.NewGuid(),
          Sequence = 1,
          Job = "Pick Up",
        },
        new()
        {
          Id = Guid.NewGuid(),
          Sequence = 2,
          Job = "Drop Off",
          DeliveredAt = delivered
            ? date.AddDays(-9).ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc)
            : null,
        },
      ],
    };
    db.DispatchSourceLinks.Add(
      new()
      {
        Dispatch = load,
        Provider = "test",
        ExternalId = "reviewed-load",
        ExecutionReviewReason = "Review source assignments.",
      }
    );
    await db.SaveChangesAsync();
    using var services = new PlanningTestServices(db);

    var board = (
      await services.Board.Handle(
        new(Date: date, IncludeHos: false, IncludeEta: false),
        default
      )
    ).Response!;

    Assert.Equal(visible ? 1 : 0, board.Items.Sum(x => x.Dispatches.Count));
    Assert.NotNull(
      (await db.DispatchSourceLinks.SingleAsync()).ExecutionReviewReason
    );
  }

  [Theory]
  [InlineData("in_transit", false)]
  [InlineData("assigned", true)]
  public async Task StartedLoadRemainsCurrentAcrossCalendarRolloverUntilActualDelivery(
    string status,
    bool pickedUp
  )
  {
    await using var connection = new SqliteConnection("Data Source=:memory:");
    await connection.OpenAsync();
    await using var db = new AppDbContext(
      new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connection).Options
    );
    await db.Database.EnsureCreatedAsync();
    var day = new DateOnly(2026, 9, 9);
    var truck = new Truck
    {
      Id = Guid.NewGuid(),
      ExternalId = "midnight",
      UnitNumber = "54777",
      IsActive = true,
    };
    var current = new Dispatch
    {
      Id = Guid.NewGuid(),
      LoadNumber = 1375,
      Truck = truck,
      Status = status,
      ShipDate = day.AddDays(-1),
      DeliveryDate = day,
      Stops =
      [
        new()
        {
          Id = Guid.NewGuid(),
          Sequence = 1,
          Job = "Pick Up",
          ScheduledDate = day.AddDays(-1),
          PickedUpAt = pickedUp
            ? day.AddDays(-1).ToDateTime(new TimeOnly(20, 0), DateTimeKind.Utc)
            : null,
        },
        new()
        {
          Id = Guid.NewGuid(),
          Sequence = 2,
          Job = "Drop Off",
          ScheduledDate = day,
        },
      ],
    };
    var next = new Dispatch
    {
      Id = Guid.NewGuid(),
      LoadNumber = 1373,
      Truck = truck,
      Status = "assigned",
      ShipDate = day.AddDays(1),
      DeliveryDate = day.AddDays(7),
    };
    db.Dispatches.AddRange(current, next);
    await db.SaveChangesAsync();
    using var services = new PlanningTestServices(db);
    foreach (var date in new[] { day, day.AddDays(1), day.AddDays(3) })
    {
      var board = await services.Board.Handle(
        new(
          TruckId: truck.Id,
          Date: date,
          IncludeHos: false,
          IncludeFinancials: false,
          IncludeEta: false
        ),
        default
      );
      Assert.Equal(
        new[] { 1375, 1373 },
        Assert
          .Single(board.Response!.Items)
          .Dispatches.Select(load => load.LoadNumber)
      );
    }
    current.Stops[^1].DeliveredAt = day.AddDays(3)
      .ToDateTime(new TimeOnly(2, 0), DateTimeKind.Utc);
    await db.SaveChangesAsync();
    services.Reads.Invalidate("board");
    var completed = await services.Board.Handle(
      new(
        TruckId: truck.Id,
        Date: day.AddDays(3),
        IncludeHos: false,
        IncludeFinancials: false,
        IncludeEta: false
      ),
      default
    );
    Assert.Equal(
      next.Id,
      Assert.Single(Assert.Single(completed.Response!.Items).Dispatches).Id
    );
  }

  [Fact]
  public async Task FuelPlanningCanAlsoIncludeOverdueUnstartedLoadsWhileActiveLoadsRemainOnTheNormalBoard()
  {
    await using var connection = new SqliteConnection("Data Source=:memory:");
    await connection.OpenAsync();
    await using var db = new AppDbContext(
      new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connection).Options
    );
    await db.Database.EnsureCreatedAsync();
    var date = new DateOnly(2026, 9, 9);
    var truck = new Truck
    {
      Id = Guid.NewGuid(),
      ExternalId = "overdue",
      UnitNumber = "54777",
      IsActive = true,
    };
    db.Trucks.Add(truck);
    Dispatch Load(int number, string status, int day, bool completed = false) =>
      new()
      {
        Id = Guid.NewGuid(),
        LoadNumber = number,
        Status = status,
        TruckId = truck.Id,
        ShipDate = date.AddDays(day),
        DeliveryDate = date.AddDays(day),
        Stops =
        [
          new()
          {
            Id = Guid.NewGuid(),
            Sequence = 1,
            Job = "Pick Up",
            ScheduledDate = date.AddDays(day),
          },
          new()
          {
            Id = Guid.NewGuid(),
            Sequence = 2,
            Job = "Drop Off",
            ScheduledDate = date.AddDays(day),
            DeliveredAt = completed
              ? date.AddDays(day).ToDateTime(TimeOnly.MinValue)
              : null,
          },
        ],
      };
    db.Dispatches.AddRange(
      Load(1, "in_transit", -2),
      Load(2, "assigned", -1),
      Load(3, "assigned", 1),
      Load(4, "assigned", -1, completed: true),
      Load(5, "completed", -1),
      Load(6, "planned", -1)
    );
    await db.SaveChangesAsync();
    using var services = new PlanningTestServices(db);
    async Task<int[]> Numbers(bool overdue) =>
      (
        await services.Board.Handle(
          new(
            TruckId: truck.Id,
            Date: date,
            IncludeHos: false,
            IncludeFinancials: false,
            IncludeEta: false,
            IncludeOverdue: overdue
          ),
          default
        )
      )
        .Response!.Items.SelectMany(row => row.Dispatches)
        .Select(load => load.LoadNumber)
        .ToArray();

    Assert.Equal(new[] { 1, 3 }, await Numbers(false));
    Assert.Equal(new[] { 1, 2, 3 }, await Numbers(true));
    Assert.Equal(new[] { 1, 3 }, await Numbers(false));
  }

  [Fact]
  public async Task PlannedLoadsAreOptInAndDoNotIncludeCancelledOrCompletedLoads()
  {
    await using var connection = new SqliteConnection("Data Source=:memory:");
    await connection.OpenAsync();
    await using var db = new AppDbContext(
      new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connection).Options
    );
    await db.Database.EnsureCreatedAsync();
    var number = 1;
    foreach (
      var status in new[]
      {
        "planned",
        "unassigned",
        "assigned",
        "completed",
        "cancelled",
        "sent",
      }
    )
      db.Dispatches.Add(
        new Dispatch
        {
          Id = Guid.NewGuid(),
          LoadNumber = number++,
          Status = status,
          ShipDate = new(2026, 9, 8),
          DeliveryDate = new(2026, 9, 9),
        }
      );
    await db.SaveChangesAsync();
    using var cache = new ReadCache(
      Options.Create(new SynchronizationOptions())
    );
    using var services = new PlanningTestServices(db, reads: cache);
    var handler = services.Board;
    var normal = await handler.Handle(new(Date: new(2026, 9, 7)), default);
    Assert.Equal(
      "assigned",
      Assert.Single(normal.Response!.Items.SelectMany(x => x.Dispatches)).Status
    );
    var planned = await handler.Handle(
      new(Date: new(2026, 9, 7), IncludePlanned: true),
      default
    );
    Assert.Equal(
      new[] { "assigned", "planned", "unassigned" },
      planned
        .Response!.Items.SelectMany(x => x.Dispatches)
        .Select(x => x.Status)
        .Order()
        .ToArray()
    );
    var again = await handler.Handle(new(Date: new(2026, 9, 7)), default);
    Assert.Single(again.Response!.Items.SelectMany(x => x.Dispatches));
  }

  [Fact]
  public async Task CachedIndexDoesNotCacheMutableDetailsAndInvalidatesWithBoard()
  {
    await using var connection = new SqliteConnection("Data Source=:memory:");
    await connection.OpenAsync();
    await using var db = new AppDbContext(
      new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connection).Options
    );
    await db.Database.EnsureCreatedAsync();
    using var cache = new ReadCache(
      Options.Create(new SynchronizationOptions())
    );
    var truck = new Truck
    {
      Id = Guid.NewGuid(),
      ExternalId = "cached",
      UnitNumber = "54777",
      IsActive = true,
    };
    var load = new Dispatch
    {
      Id = Guid.NewGuid(),
      TruckId = truck.Id,
      Status = "assigned",
      LoadNumber = 9876,
      Stops =
      [
        new()
        {
          Id = Guid.NewGuid(),
          Sequence = 1,
          Notes = "initial",
        },
      ],
    };
    db.Trucks.Add(truck);
    db.Dispatches.Add(load);
    await db.SaveChangesAsync();
    using var services = new PlanningTestServices(db, reads: cache);
    var handler = services.Board;
    var first = (
      await handler.Handle(new(IncludeHos: false), default)
    ).Response!;
    first.Items[0].TruckNumber = "response mutation";
    first.Items[0].Dispatches.Clear();
    load.Stops[0].Notes = "fresh details";
    await db.SaveChangesAsync();
    var second = (
      await handler.Handle(new(Search: "54777", IncludeHos: false), default)
    ).Response!;
    Assert.Equal("54777", Assert.Single(second.Items).TruckNumber);
    Assert.Equal(
      "fresh details",
      Assert.Single(Assert.Single(second.Items[0].Dispatches).Stops).Notes
    );
    truck.UnitNumber = "60000";
    await db.SaveChangesAsync();
    cache.Invalidate("board");
    var refreshed = (
      await handler.Handle(new(Search: "60000", IncludeHos: false), default)
    ).Response!;
    Assert.Equal("60000", Assert.Single(refreshed.Items).TruckNumber);
  }

  [Fact]
  public async Task SearchUsesTruckPrefixAndFindsLoadsOutsideTheFirstPage()
  {
    await using var connection = new SqliteConnection("Data Source=:memory:");
    await connection.OpenAsync();
    await using var db = new AppDbContext(
      new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connection).Options
    );
    await db.Database.EnsureCreatedAsync();
    var first = new Truck
    {
      Id = Guid.NewGuid(),
      ExternalId = "first",
      UnitNumber = "11005",
      IsActive = true,
    };
    var second = new Truck
    {
      Id = Guid.NewGuid(),
      ExternalId = "second",
      UnitNumber = "54777",
      IsActive = true,
    };
    db.Trucks.AddRange(first, second);
    db.Dispatches.Add(
      new Dispatch
      {
        Id = Guid.NewGuid(),
        TruckId = first.Id,
        Status = "assigned",
        LoadNumber = 5555,
        OrderNumber = "5000",
      }
    );
    db.Dispatches.Add(
      new Dispatch
      {
        Id = Guid.NewGuid(),
        TruckId = second.Id,
        Status = "assigned",
        LoadNumber = 9876,
        Stops =
        [
          new()
          {
            Id = Guid.NewGuid(),
            Sequence = 1,
            Address = "Full address",
            Notes = "Full notes",
          },
        ],
      }
    );
    await db.SaveChangesAsync();
    using var services = new PlanningTestServices(db);
    var handler = services.Board;
    var prefix = (
      await handler.Handle(new(Search: "5", IncludeHos: false), default)
    ).Response!;
    Assert.Equal(second.Id, Assert.Single(prefix.Items).TruckId);
    var load = (
      await handler.Handle(
        new(Search: "987", PageSize: 1, IncludeHos: false),
        default
      )
    ).Response!;
    var row = Assert.Single(load.Items);
    Assert.Equal(second.Id, row.TruckId);
    Assert.Equal(
      "Full notes",
      Assert.Single(Assert.Single(row.Dispatches).Stops).Notes
    );
    var exact = (
      await handler.Handle(new(Search: "11005", IncludeHos: false), default)
    ).Response!;
    Assert.Equal(first.Id, Assert.Single(exact.Items).TruckId);
  }

  [Fact]
  public async Task FilteredBoardMatchesFullBoardForNumberAndStopAssignments()
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
      ExternalId = "target",
      UnitNumber = "AB101",
      IsActive = true,
    };
    var other = new Truck
    {
      Id = Guid.NewGuid(),
      ExternalId = "other",
      UnitNumber = "102",
      IsActive = true,
    };
    db.Trucks.AddRange(truck, other);
    db.Dispatches.AddRange(
      new Dispatch
      {
        Id = Guid.NewGuid(),
        LoadNumber = 1,
        Status = "assigned",
        TruckNumber = " ab101 ",
      },
      new Dispatch
      {
        Id = Guid.NewGuid(),
        LoadNumber = 2,
        Status = "assigned",
        TruckId = other.Id,
        TruckNumber = "AB101",
      },
      new Dispatch
      {
        Id = Guid.NewGuid(),
        LoadNumber = 3,
        Status = "assigned",
        TruckId = other.Id,
        Stops =
        [
          new()
          {
            Id = Guid.NewGuid(),
            Sequence = 1,
            TruckNumber = " ab101 ",
          },
        ],
      },
      new Dispatch
      {
        Id = Guid.NewGuid(),
        LoadNumber = 4,
        Status = "assigned",
        TruckId = truck.Id,
        Stops =
        [
          new()
          {
            Id = Guid.NewGuid(),
            Sequence = 1,
            TruckId = truck.Id,
          },
        ],
      }
    );
    await db.SaveChangesAsync();
    using var services = new PlanningTestServices(db);
    var handler = services.Board;
    var full = (
      await handler.Handle(new(IncludeHos: false), default)
    ).Response!;
    var filtered = (
      await handler.Handle(new(TruckId: truck.Id, IncludeHos: false), default)
    ).Response!;
    var row = Assert.Single(filtered.Items);
    Assert.Equal(new[] { 1, 3, 4 }, row.Dispatches.Select(x => x.LoadNumber));
    Assert.Equal(
      full.Items.Single(x => x.TruckId == truck.Id)
        .Dispatches.Select(x => x.Id),
      row.Dispatches.Select(x => x.Id)
    );
  }

  [Fact]
  public async Task BoardKeepsOverdueActiveLoadsAndHidesFinishedAndPastUnstartedLoads()
  {
    await using var connection = new SqliteConnection("Data Source=:memory:");
    await connection.OpenAsync();
    await using var db = new AppDbContext(
      new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connection).Options
    );
    await db.Database.EnsureCreatedAsync();
    var date = new DateOnly(2026, 9, 5);
    var driver = new Driver
    {
      Id = Guid.NewGuid(),
      ExternalId = "driver",
      Name = "Current Driver",
    };
    var trailer = new Trailer
    {
      Id = Guid.NewGuid(),
      ExternalId = "trailer",
      UnitNumber = "T100",
    };
    var truck = new Truck
    {
      Id = Guid.NewGuid(),
      ExternalId = "truck",
      UnitNumber = "101",
      IsActive = true,
      Driver = driver,
      Trailer = trailer,
    };
    var available = new Truck
    {
      Id = Guid.NewGuid(),
      ExternalId = "available",
      UnitNumber = "102",
      IsActive = true,
    };
    db.Trucks.AddRange(truck, available);
    Dispatch Load(int number, string status, int start, int end) =>
      new()
      {
        Id = Guid.NewGuid(),
        LoadNumber = number,
        TruckId = truck.Id,
        TruckNumber = "101",
        Status = status,
        ShipDate = date.AddDays(start),
        DeliveryDate = date.AddDays(end),
        DriverName = "Planned Driver",
        Stops =
        [
          new()
          {
            Id = Guid.NewGuid(),
            Sequence = 1,
            Job = "Pick Up",
            City = "Chicago",
            ScheduledDate = date.AddDays(start),
          },
          new()
          {
            Id = Guid.NewGuid(),
            Sequence = 2,
            Job = "Drop Off",
            City = "Toronto",
            ScheduledDate = date.AddDays(end),
          },
        ],
      };
    var completedActual = Load(6, "assigned", 0, 0);
    completedActual.Stops[1].DeliveredAt = date.ToDateTime(new TimeOnly(10, 0));
    db.Dispatches.AddRange(
      Load(1, "in_transit", -1, 1),
      Load(2, "assigned", 2, 3),
      Load(3, "assigned", 1, 2),
      Load(4, "completed", 0, 1),
      Load(5, "assigned", -2, -1),
      completedActual,
      Load(7, "cancelled", 1, 2),
      Load(8, "sent", 0, 1),
      Load(9, "in_transit", -3, -1)
    );
    await db.SaveChangesAsync();
    using var services = new PlanningTestServices(db);
    var handler = services.Board;
    var board = (
      await handler.Handle(new(PageSize: 1, Date: date), default)
    ).Response!;
    Assert.Equal(2, board.TotalCount);
    var row = Assert.Single(board.Items);
    Assert.Equal(truck.Id, row.TruckId);
    Assert.Equal("Current Driver", row.DriverName);
    Assert.Equal("T100", row.TrailerNumber);
    Assert.Equal(
      new[] { 9, 1, 3, 2 },
      row.Dispatches.Select(x => x.LoadNumber)
    );
    Assert.Equal("Chicago", row.Dispatches[0].Stops[0].City);
    Assert.Equal("Toronto", row.Dispatches[0].Stops[1].City);
    var empty = (
      await handler.Handle(new(Page: 2, PageSize: 1, Date: date), default)
    ).Response!;
    Assert.Empty(Assert.Single(empty.Items).Dispatches);
    var searched = (
      await handler.Handle(new(Search: "Toronto", Date: date), default)
    ).Response!;
    Assert.Equal(4, Assert.Single(searched.Items).Dispatches.Count);
  }

  [Fact]
  public async Task BoardFindsStopAssignmentsDeduplicatesTruckAndKeepsUnassignedLoads()
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
      ExternalId = "truck",
      UnitNumber = "101",
      IsActive = true,
    };
    db.Trucks.Add(truck);
    db.Dispatches.Add(
      new Dispatch
      {
        Id = Guid.NewGuid(),
        LoadNumber = 1,
        Status = "assigned",
        TruckNumber = "101",
        Stops =
        [
          new()
          {
            Id = Guid.NewGuid(),
            TruckId = truck.Id,
            Sequence = 1,
            DriverName = "Stop Driver",
            TrailerNumber = "T2",
          },
          new()
          {
            Id = Guid.NewGuid(),
            TruckId = truck.Id,
            Sequence = 2,
          },
        ],
      }
    );
    db.Dispatches.Add(
      new Dispatch
      {
        Id = Guid.NewGuid(),
        LoadNumber = 2,
        Status = "assigned",
      }
    );
    await db.SaveChangesAsync();
    using var services = new PlanningTestServices(db);
    var handler = services.Board;
    var board = (await handler.Handle(new(), default)).Response!;
    Assert.Equal(2, board.TotalCount);
    var row = board.Items.Single(x => x.TruckId == truck.Id);
    Assert.Single(row.Dispatches);
    Assert.Equal("Stop Driver", row.DriverName);
    Assert.Equal("T2", row.TrailerNumber);
    Assert.Equal(
      2,
      Assert
        .Single(board.Items.Single(x => x.Key == "unassigned").Dispatches)
        .LoadNumber
    );
    var filtered = (
      await handler.Handle(new(TruckId: truck.Id), default)
    ).Response!;
    Assert.Equal(truck.Id, Assert.Single(filtered.Items).TruckId);
  }
}
