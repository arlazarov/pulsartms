using Application.Features.Dispatch.Queries;
using Domain.Entities.Dispatch;
using Domain.Entities.Execution;
using Domain.Entities.Fleet;
using Infrastructure.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Server.Tests.Support;
using Load = Domain.Entities.Dispatch.Dispatch;

namespace Server.Tests.Dispatch;

// Stage 4b of docs/architecture/current-work.md. A load is completed when
// its accepted execution's legs are all completed; any other load when
// closed, or when its cargo is delivered and the truck's work finished.
// Cargo delivery alone may come first (a trailer still to drop). The
// expected answers below follow that rule, shape by shape; the C# owner
// (DispatchResponse.Completed/CargoDelivered, read as the Completed tab
// reads the stored rows) and the tab's database filter
// (CompletedLoads.Filter) must both give them - on SQLite always, on
// PostgreSQL where a test fixture is recorded.
[Trait("Category", "Dispatch")]
[Trait("Kind", "Integration")]
public sealed class CompletedLoadsParityTests
{
  private static readonly DateTime At = new(
    2026,
    9,
    20,
    12,
    0,
    0,
    DateTimeKind.Utc
  );

  private sealed record Shape(
    string Name,
    bool Completed,
    bool Delivered,
    Stop[] Stops,
    string Status = "assigned",
    string[]? Legs = null
  );

  private static IEnumerable<Shape> Shapes() =>
    [
      new("closed", true, true, [], Status: "completed"),
      new("delivered", true, true, [Pickup(done: true), Delivery(At)]),
      new("delivery open", false, false, [Pickup(done: true), Delivery(null)]),
      new(
        "trailer drop open after delivery",
        false,
        true,
        [Pickup(done: true), Delivery(At), Drop(done: false)]
      ),
      new(
        "trailer drop done after delivery",
        true,
        true,
        [Pickup(done: true), Delivery(At), Drop(done: true)]
      ),
      new(
        "driver-only stop after delivery",
        true,
        true,
        [Pickup(done: true), Delivery(At), DriverOnly()]
      ),
      new(
        "multi-drop, last open",
        false,
        false,
        [Pickup(done: true), Delivery(At), Delivery(null)]
      ),
      new(
        "multi-drop, last delivered",
        true,
        true,
        [Pickup(done: true), Delivery(At), Delivery(At)]
      ),
      new(
        "confirmed by hand, pickup open",
        false,
        false,
        [Pickup(done: false), Delivery(null, confirmed: true)]
      ),
      new(
        "confirmed by hand, pickup done",
        true,
        true,
        [Pickup(done: true), Delivery(null, confirmed: true)]
      ),
      new(
        "confirmed by hand, pickup done, later drop open",
        false,
        true,
        [Pickup(done: true), Delivery(null, confirmed: true), Drop(done: false)]
      ),
      new(
        "overridden not done despite a record",
        false,
        false,
        [Pickup(done: true), Delivery(At, overridden: false)]
      ),
      new(
        "overridden done, pickup open",
        true,
        true,
        [Pickup(done: false), Delivery(null, overridden: true)]
      ),
      new(
        "delivery set by the dispatcher's action",
        true,
        true,
        [Pickup(done: true), new("Stop", Departed: At, Action: "Drop Off")]
      ),
      new(
        "No truck carried to a later stop without a truck",
        true,
        true,
        [
          Pickup(done: true),
          Delivery(At),
          DriverOnly(),
          new("Stop", NoTruck: true),
        ]
      ),
      new(
        "No truck ended by a stop with a truck",
        false,
        true,
        [Pickup(done: true), Delivery(At), DriverOnly(), Drop(done: false)]
      ),
      new(
        "an open stop before the planning start",
        true,
        true,
        [
          new("Pick Up", NoTruck: true),
          Pickup(done: true, start: true),
          Delivery(At),
        ]
      ),
      new(
        "legs all completed, source stops open",
        true,
        true,
        [Pickup(done: false), Delivery(null)],
        Legs: ["completed", "completed"]
      ),
      new(
        "a cancelled leg beside a completed one",
        true,
        true,
        [Pickup(done: false), Delivery(null)],
        Legs: ["cancelled", "completed"]
      ),
      new(
        "all legs cancelled, the source delivered",
        true,
        true,
        [Pickup(done: true), Delivery(At)],
        Legs: ["cancelled", "cancelled"]
      ),
      new(
        "all legs cancelled, the source open",
        false,
        false,
        [Pickup(done: true), Delivery(null)],
        Legs: ["cancelled"]
      ),
      new(
        "source says delivered, a leg still open",
        false,
        true,
        [Pickup(done: true), Delivery(At)],
        Status: "completed",
        Legs: ["completed", "active"]
      ),
    ];

  [Fact]
  public async Task TheOwnerAndTheFilterAgreeOnSqlite()
  {
    await using var connection = new SqliteConnection("Data Source=:memory:");
    await connection.OpenAsync();
    await using var db = new AppDbContext(
      new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connection).Options
    );
    await db.Database.EnsureCreatedAsync();
    await CheckAsync(db);
  }

  [RequiresPostgresFact]
  public async Task TheOwnerAndTheFilterAgreeOnPostgres()
  {
    await using var fixture = await PostgresFixture.CreateAsync();
    await CheckAsync(fixture.Connect());
  }

  private static async Task CheckAsync(AppDbContext db)
  {
    var truck = new Truck
    {
      Id = Guid.NewGuid(),
      ExternalId = "parity",
      UnitNumber = "Parity",
      IsActive = true,
    };
    db.Trucks.Add(truck);
    var shapes = Shapes()
      .Select((shape, i) => (Shape: shape, Load: Load(db, truck, i, shape)))
      .ToList();
    await db.SaveChangesAsync();
    db.ChangeTracker.Clear();

    // The C# owner, as the Completed tab reads a page of source rows.
    var owner = (
      await db
        .Dispatches.AsNoTracking()
        .Select(DispatchProjection.Details)
        .ToListAsync()
    )
      .Select(x => DispatchProjection.Complete(x))
      .ToList();
    await CompletedLoads.MarkExecutionAsync(
      db.LoadExecutionLegs,
      owner,
      default
    );
    var byId = owner.ToDictionary(x => x.Id);
    var filtered = (
      await db
        .Dispatches.AsNoTracking()
        .Where(CompletedLoads.Filter(db.LoadExecutionLegs))
        .Select(x => x.Id)
        .ToListAsync()
    ).ToHashSet();

    Assert.Equal(
      shapes.Select(x => (x.Shape.Name, x.Shape.Completed, x.Shape.Delivered)),
      shapes.Select(x =>
        (
          x.Shape.Name,
          byId[x.Load.Id].Completed,
          byId[x.Load.Id].CargoDelivered
        )
      )
    );
    Assert.Equal(
      shapes.Select(x => (x.Shape.Name, x.Shape.Completed)),
      shapes.Select(x => (x.Shape.Name, filtered.Contains(x.Load.Id)))
    );
  }

  private sealed record Stop(
    string Job,
    DateTime? PickedUp = null,
    DateTime? Delivered = null,
    DateTime? Departed = null,
    DateTime? Confirmed = null,
    bool? Override = null,
    string? Action = null,
    string? StateAfter = null,
    bool NoTruck = false,
    bool Start = false
  );

  private static Stop Pickup(bool done, bool start = false) =>
    new("Pick Up", PickedUp: done ? At : null, Start: start);

  private static Stop Delivery(
    DateTime? delivered,
    bool confirmed = false,
    bool? overridden = null
  ) =>
    new(
      "Drop Off",
      Delivered: delivered,
      Confirmed: confirmed ? At : null,
      Override: overridden
    );

  private static Stop Drop(bool done) =>
    new(
      "Stop",
      Departed: done ? At : null,
      Action: "Drop trailer",
      StateAfter: "Bobtail"
    );

  private static Stop DriverOnly() =>
    new("Stop", Action: "Driver start", StateAfter: "No truck", NoTruck: true);

  private static Load Load(
    AppDbContext db,
    Truck truck,
    int number,
    Shape shape
  )
  {
    var id = Guid.NewGuid();
    var stops = shape
      .Stops.Select(
        (stop, i) =>
          new DispatchStop
          {
            Id = Guid.NewGuid(),
            DispatchId = id,
            Sequence = i + 1,
            Job = stop.Job,
            TruckId = stop.NoTruck ? null : truck.Id,
            PickedUpAt = stop.PickedUp,
            DeliveredAt = stop.Delivered,
            DepartedAt = stop.Departed,
            ManualCompletedAt = stop.Confirmed,
            CompletionOverride = stop.Override,
            ManualAction = stop.Action,
            ManualStateAfter = stop.StateAfter,
          }
      )
      .ToList();
    var load = new Load
    {
      Id = id,
      LoadNumber = 9000 + number,
      Status = shape.Status,
      TruckId = truck.Id,
      PlanningFromStopId = shape
        .Stops.Zip(stops)
        .FirstOrDefault(x => x.First.Start)
        .Second?.Id,
      Stops = stops,
    };
    db.Dispatches.Add(load);
    foreach (var (status, i) in (shape.Legs ?? []).Select((x, i) => (x, i)))
    {
      var leg = new ExecutionLeg
      {
        Id = Guid.NewGuid(),
        Trip = new() { Id = Guid.NewGuid() },
        TruckId = truck.Id,
        Status = status,
        Revision = 1,
      };
      db.ExecutionLegs.Add(leg);
      db.LoadExecutionLegs.Add(
        new()
        {
          Id = Guid.NewGuid(),
          DispatchId = id,
          ExecutionLeg = leg,
          ExecutionLegId = leg.Id,
          Sequence = i,
        }
      );
    }
    return load;
  }
}
