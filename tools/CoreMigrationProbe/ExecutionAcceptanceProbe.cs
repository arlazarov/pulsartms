using Application.Features.Execution.Models;
using Application.Features.Execution.Services;
using Domain.Entities.Dispatch;
using Domain.Entities.Execution;
using Domain.Entities.Fleet;
using Domain.Entities.Mileage;
using Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Load = Domain.Entities.Dispatch.Dispatch;

namespace CoreMigrationProbe;

internal static class ExecutionAcceptanceProbe
{
  public static async Task VerifyAsync(AppDbContext db)
  {
    var now = DateTime.UtcNow;
    var truck = new Truck
    {
      Id = Guid.NewGuid(),
      ExternalId = "acceptance-fixture",
      UnitNumber = "acceptance-fixture",
    };
    var source = new Load
    {
      Id = Guid.NewGuid(),
      LoadNumber = 2,
      Status = "in_transit",
      TruckId = truck.Id,
      Stops = Enumerable
        .Range(1, 3)
        .Select(i => new DispatchStop
        {
          Id = Guid.NewGuid(),
          Sequence = i,
          Job = i == 3 ? "Drop Off" : "Pick Up",
          Address = $"Fixture facility {i}",
          TruckId = truck.Id,
        })
        .ToList(),
    };
    var leg = new ExecutionLeg
    {
      Id = Guid.NewGuid(),
      TruckId = truck.Id,
      Trip = new() { Id = Guid.NewGuid() },
      Revision = 1,
      Status = "active",
      RecordedAt = now,
    };
    foreach (var stop in source.Stops)
      stop.DispatchId = source.Id;
    ExecutionStopRows.Replace(leg, source.Stops);
    leg.Loads.Add(
      new()
      {
        Id = Guid.NewGuid(),
        DispatchId = source.Id,
        StartVisitId = source.Stops[0].Id,
        EndVisitId = source.Stops[^1].Id,
      }
    );
    var movement = new Movement
    {
      Id = Guid.NewGuid(),
      IdempotencyKey = Guid.NewGuid(),
      TruckId = truck.Id,
      ExecutionLegId = leg.Id,
      Origin = "native-route",
      FromVisitId = source.Stops[0].Id,
      ToVisitId = source.Stops[1].Id,
      PlannedMiles = 10,
      Revision = 1,
    };
    db.Trucks.Add(truck);
    db.Dispatches.Add(source);
    db.ExecutionLegs.Add(leg);
    db.Movements.Add(movement);
    await db.SaveChangesAsync();
    var inserted = new DispatchStop
    {
      Id = Guid.NewGuid(),
      DispatchId = source.Id,
      Job = "Waypoint",
      Address = "Fixture inserted facility",
      TruckId = truck.Id,
    };
    source.Stops.Insert(1, inserted);
    for (var i = 0; i < source.Stops.Count; i++)
      source.Stops[i].Sequence = i + 1;
    db.DispatchStops.Add(inserted);
    await using (var tx = await db.Database.BeginTransactionAsync())
    {
      await ExecutionSourceReconciliation.ApplyAsync(
        db,
        [source],
        now,
        default
      );
      await db.SaveChangesAsync();
      Require(
        leg.Revision == 2
          && leg.Stops.Count == 4
          && leg.SourceReviewReason is null
          && movement.PlannedSuperseded,
        "Import must accept the visit and retire the old path together."
      );
      Require(
        (
          await ExecutionSourceReconciliation.ApplyAsync(
            db,
            [source],
            now,
            default
          )
        ).Count == 0,
        "An identical import must not create another accepted revision."
      );
      await tx.CommitAsync();
    }
    var stops = ExecutionStopRows.Read(leg);
    var actor = await db.Users.Select(x => x.Id).SingleAsync();
    var key = Guid.NewGuid();
    stops[^1].Address = "Fixture manually accepted facility";
    await using (var tx = await db.Database.BeginTransactionAsync())
    {
      await ExecutionAcceptance.ApplyAsync(
        db,
        [new(leg, stops)],
        "workspace-updated",
        actor,
        key,
        now,
        default
      );
      await db.SaveChangesAsync();
      await tx.CommitAsync();
    }
    var history = await db
      .ExecutionLegRevisions.Where(x => x.ExecutionLegId == leg.Id)
      .OrderBy(x => x.Revision)
      .ToListAsync();
    var requests = await db
      .ExecutionPlanningChanges.Where(x => x.ExecutionLegId == leg.Id)
      .OrderBy(x => x.AssignmentRevision)
      .ToListAsync();
    Require(
      history.Count == 2
        && history[0].RecordedBy is null
        && history[1].RecordedBy == actor
        && history[1].CorrelationId == key
        && requests.Select(x => x.AssignmentRevision).SequenceEqual([2L, 3L]),
      "Both acceptance paths must retain actor, revision and durable work."
    );
    await using (var tx = await db.Database.BeginTransactionAsync())
    {
      stops[^1].Address = "Fixture rolled-back facility";
      await ExecutionAcceptance.ApplyAsync(
        db,
        [new(leg, stops)],
        "workspace-updated",
        actor,
        Guid.NewGuid(),
        now,
        default
      );
      await db.SaveChangesAsync();
      await tx.RollbackAsync();
    }
    db.ChangeTracker.Clear();
    var saved = await db.ExecutionLegs.SingleAsync(x => x.Id == leg.Id);
    Require(
      saved.Revision == 3
        && saved.Stops.OrderBy(x => x.Position).Last().Address
          == "Fixture manually accepted facility"
        && await db.ExecutionLegRevisions.CountAsync(x =>
          x.ExecutionLegId == leg.Id
        ) == 2
        && await db.ExecutionPlanningChanges.CountAsync(x =>
          x.ExecutionLegId == leg.Id
        ) == 2,
      "Rollback must leave accepted facts, history and queue unchanged."
    );
    Console.WriteLine(
      "PostgreSQL stop acceptance passed: import/manual ownership, "
        + "obsolete mileage, idempotent replay, actor history "
        + "and atomic rollback."
    );
  }

  private static void Require(bool condition, string message)
  {
    if (!condition)
      throw new InvalidOperationException(message);
  }
}
