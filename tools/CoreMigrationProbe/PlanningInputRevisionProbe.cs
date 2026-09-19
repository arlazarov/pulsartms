using Domain.Entities.Dispatch;
using Domain.Entities.Execution;
using Domain.Entities.Fleet;
using Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Load = Domain.Entities.Dispatch.Dispatch;

namespace CoreMigrationProbe;

internal static class PlanningInputRevisionProbe
{
  public static async Task VerifyAsync(AppDbContext db)
  {
    db.ChangeTracker.Clear();
    await using var transaction = await db.Database.BeginTransactionAsync();
    var trucks = Enumerable
      .Range(0, 3)
      .Select(_ => new Truck
      {
        Id = Guid.NewGuid(),
        ExternalId = Guid.NewGuid().ToString(),
        UnitNumber = Guid.NewGuid().ToString(),
        IsActive = true,
      })
      .ToArray();
    db.Trucks.AddRange(trucks);
    var source = new Load
    {
      Id = Guid.NewGuid(),
      LoadNumber = await db.Dispatches.MaxAsync(x => x.LoadNumber) + 1,
      TruckNumber = "  " + trucks[0].UnitNumber.ToUpperInvariant() + "  ",
      Stops = [new() { Id = Guid.NewGuid(), TruckId = trucks[0].Id }],
    };
    db.Dispatches.Add(source);
    await db.SaveChangesAsync();

    await ChangesAsync(
      async () =>
      {
        await db
          .Dispatches.Where(x => x.Id == source.Id)
          .ExecuteUpdateAsync(s => s.SetProperty(x => x.Price, 1200m));
      },
      [trucks[0].Id]
    );
    await ChangesAsync(
      async () =>
      {
        await db
          .Dispatches.Where(x => x.Id == source.Id)
          .ExecuteUpdateAsync(s =>
            s.SetProperty(x => x.TruckNumber, trucks[1].UnitNumber)
          );
      },
      [trucks[0].Id, trucks[1].Id]
    );
    await ChangesAsync(
      async () =>
      {
        await db
          .DispatchStops.Where(x => x.DispatchId == source.Id)
          .ExecuteUpdateAsync(s => s.SetProperty(x => x.TruckId, trucks[1].Id));
      },
      [trucks[0].Id, trucks[1].Id]
    );

    var trip = new Trip { Id = Guid.NewGuid() };
    var leg = new ExecutionLeg
    {
      Id = Guid.NewGuid(),
      Trip = trip,
      TripId = trip.Id,
      TruckId = trucks[2].Id,
      Status = "planned",
      Revision = 1,
      Loads =
      [
        new()
        {
          Id = Guid.NewGuid(),
          DispatchId = source.Id,
          StartVisitId = source.Stops[0].Id,
          EndVisitId = source.Stops[0].Id,
        },
      ],
    };
    db.ExecutionLegs.Add(leg);
    await db.SaveChangesAsync();
    await ChangesAsync(
      async () =>
      {
        await db
          .DispatchStops.Where(x => x.DispatchId == source.Id)
          .ExecuteUpdateAsync(s => s.SetProperty(x => x.Notes, "Changed"));
      },
      [trucks[1].Id, trucks[2].Id]
    );
    await ChangesAsync(
      async () =>
      {
        await db
          .ExecutionLegs.Where(x => x.Id == leg.Id)
          .ExecuteUpdateAsync(s => s.SetProperty(x => x.TruckId, trucks[0].Id));
      },
      trucks.Select(x => x.Id).ToArray()
    );
    await ChangesAsync(
      async () =>
      {
        await db
          .LoadExecutionLegs.Where(x => x.ExecutionLegId == leg.Id)
          .ExecuteDeleteAsync();
      },
      [trucks[0].Id, trucks[1].Id]
    );
    await ChangesAsync(
      async () =>
      {
        db.TruckPlanningProfiles.Add(
          new()
          {
            Id = Guid.NewGuid(),
            TruckId = trucks[2].Id,
            SettingsJson = "{}",
          }
        );
        await db.SaveChangesAsync();
      },
      [trucks[2].Id]
    );
    await ChangesAsync(
      async () =>
      {
        await db
          .Trucks.Where(x => x.Id == trucks[0].Id)
          .ExecuteUpdateAsync(s => s.SetProperty(x => x.IsActive, false));
      },
      [trucks[0].Id]
    );
    await ChangesAsync(
      async () =>
      {
        await db
          .Trucks.Where(x => x.Id == trucks[0].Id)
          .ExecuteUpdateAsync(s => s.SetProperty(x => x.UnitNumber, "Renamed"));
      },
      [trucks[0].Id],
      global: true
    );
    await ChangesAsync(
      async () =>
      {
        db.Drivers.Add(
          new()
          {
            Id = Guid.NewGuid(),
            ExternalId = Guid.NewGuid().ToString(),
            Name = "Revision fixture",
            IsActive = true,
          }
        );
        await db.SaveChangesAsync();
      },
      [],
      global: true
    );

    var registered = await db
      .Database.SqlQueryRaw<string>(
        """
        SELECT c.relname AS "Value" FROM pg_trigger t
        JOIN pg_class c ON c.oid = t.tgrelid
        JOIN pg_namespace n ON n.oid = c.relnamespace
        WHERE n.nspname = 'public' AND t.tgname = 'pulsr_planning_inputs'
          AND t.tgenabled = 'O'
        """
      )
      .ToArrayAsync();
    Require(registered.Length == 17, "Every planning writer needs a guard.");
    await transaction.RollbackAsync();
    db.ChangeTracker.Clear();
    Console.WriteLine(
      "Planning input revisions passed: number-only work, source and native "
        + "reassignment, removed links, missing profiles, fleet changes and "
        + "all writer trigger registrations."
    );

    async Task ChangesAsync(
      Func<Task> change,
      IReadOnlyCollection<Guid> expected,
      bool global = false
    )
    {
      var before = await RevisionsAsync();
      await change();
      var after = await RevisionsAsync();
      foreach (var truck in trucks)
        Require(
          (after[truck.Id] > before[truck.Id]) == expected.Contains(truck.Id),
          "A writer must advance every affected truck and no unrelated truck."
        );
      Require(
        (after[Guid.Empty] > before[Guid.Empty]) == global,
        "Only global membership or settings changes advance the fleet revision."
      );
    }

    Task<Dictionary<Guid, long>> RevisionsAsync() =>
      db
        .PlanningInputRevisions.AsNoTracking()
        .ToDictionaryAsync(x => x.TruckId, x => x.Revision);
  }

  private static void Require(bool condition, string message)
  {
    if (!condition)
      throw new InvalidOperationException(message);
  }
}
