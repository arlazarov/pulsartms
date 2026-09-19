using Application.Features.Routing.Exceptions;
using Domain.Entities.Fleet;
using Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Load = Domain.Entities.Dispatch.Dispatch;

namespace CoreMigrationProbe;

internal static class PlanningPublicationProbe
{
  public static async Task VerifyAsync(AppDbContext db)
  {
    db.ChangeTracker.Clear();
    var trucks = Enumerable
      .Range(0, 2)
      .Select(_ => new Truck
      {
        Id = Guid.NewGuid(),
        ExternalId = Guid.NewGuid().ToString(),
        UnitNumber = Guid.NewGuid().ToString(),
        IsActive = true,
      })
      .ToArray();
    var firstNumber = await db.Dispatches.MaxAsync(x => x.LoadNumber) + 1;
    var loads = trucks
      .Select(
        (truck, index) =>
          new Load
          {
            Id = Guid.NewGuid(),
            LoadNumber = firstNumber + index,
            TruckId = truck.Id,
            TruckNumber = truck.UnitNumber,
          }
      )
      .ToArray();
    db.Trucks.AddRange(trucks);
    db.Dispatches.AddRange(loads);
    var rateId = FuelExchangeRateStore.Id;
    if (!await db.SynchronizationCheckpoints.AnyAsync(x => x.Id == rateId))
      db.SynchronizationCheckpoints.Add(new() { Id = rateId });
    await db.SaveChangesAsync();
    var connection = db.Database.GetConnectionString()!;
    await using (
      var scope = await new PlanningPublicationScope(db).BeginAsync(
        trucks[0].Id,
        default
      )
    )
    {
      await using (var other = Context())
      await using (
        var independent = await new PlanningPublicationScope(other).BeginAsync(
          trucks[1].Id,
          default
        )
      )
      {
        await other
          .Dispatches.Where(x => x.Id == loads[1].Id)
          .ExecuteUpdateAsync(s =>
            s.SetProperty(x => x.OrderNumber, "Independent")
          );
        await independent.CommitAsync();
      }
      await RejectPublicationAsync(trucks[0].Id);
      await RejectPublicationAsync(null);
      await RejectWriterAsync(async writer =>
      {
        await writer
          .Dispatches.Where(x => x.Id == loads[0].Id)
          .ExecuteUpdateAsync(s => s.SetProperty(x => x.OrderNumber, "Stale"));
      });
      await RejectWriterAsync(async writer =>
      {
        writer.Dispatches.Add(
          new()
          {
            Id = Guid.NewGuid(),
            LoadNumber = firstNumber + 2,
            TruckNumber = trucks[0].UnitNumber,
          }
        );
        await writer.SaveChangesAsync();
      });
      await RejectWriterAsync(async writer =>
      {
        writer.TruckPlanningProfiles.Add(
          new()
          {
            Id = Guid.NewGuid(),
            TruckId = trucks[0].Id,
            SettingsJson = "{}",
          }
        );
        await writer.SaveChangesAsync();
      });
      await RejectWriterAsync(async writer =>
      {
        await writer
          .Trucks.Where(x => x.Id == trucks[1].Id)
          .ExecuteUpdateAsync(s =>
            s.SetProperty(x => x.UnitNumber, "New name")
          );
      });
      await RejectWriterAsync(async writer =>
      {
        await writer
          .SynchronizationCheckpoints.Where(x => x.Id == rateId)
          .ExecuteUpdateAsync(s =>
            s.SetProperty(x => x.StateJson, "{\"rate\":2}")
          );
      });
      await using var checkpoint = Context();
      checkpoint.SynchronizationCheckpoints.Add(
        new() { Id = Guid.NewGuid(), Owner = "unrelated-lease" }
      );
      await checkpoint.SaveChangesAsync();
      await scope.CommitAsync();
    }
    await db
      .Dispatches.Where(x => x.Id == loads[0].Id)
      .ExecuteUpdateAsync(s =>
        s.SetProperty(x => x.OrderNumber, "After commit")
      );
    Require(
      !await db.Dispatches.AnyAsync(x => x.LoadNumber == firstNumber + 2),
      "A blocked membership writer must roll back its inserted load."
    );
    await using (
      var global = await new PlanningPublicationScope(db).BeginAsync(
        null,
        default
      )
    )
    {
      await RejectPublicationAsync(trucks[1].Id);
      await RejectWriterAsync(async writer =>
      {
        await writer
          .Dispatches.Where(x => x.Id == loads[1].Id)
          .ExecuteUpdateAsync(s => s.SetProperty(x => x.OrderNumber, "Global"));
      });
    }
    Console.WriteLine(
      "Scoped planning publication passed: independent trucks progress, "
        + "same-truck writes and new membership/settings wait, fleet/rate "
        + "changes coordinate, unrelated checkpoints remain independent, "
        + "and rejected writers roll back."
    );

    AppDbContext Context() =>
      new(
        new DbContextOptionsBuilder<AppDbContext>()
          .UseNpgsql(connection, x => x.CommandTimeout(5))
          .Options
      );

    async Task RejectPublicationAsync(Guid? truckId)
    {
      await using var other = Context();
      try
      {
        await using var conflicting = await new PlanningPublicationScope(
          other
        ).BeginAsync(truckId, default);
      }
      catch (RoutePlanningException)
      {
        return;
      }
      throw new InvalidOperationException(
        "Conflicting publication must retry."
      );
    }

    async Task RejectWriterAsync(Func<AppDbContext, Task> write)
    {
      await using var writer = Context();
      await using var attempt = await writer.Database.BeginTransactionAsync();
      await writer.Database.ExecuteSqlRawAsync(
        "SET LOCAL lock_timeout = '150ms'"
      );
      try
      {
        await write(writer);
      }
      catch (Exception exception)
      {
        for (
          Exception? failure = exception;
          failure is not null;
          failure = failure.InnerException
        )
          if (
            failure is PostgresException
            {
              SqlState: PostgresErrorCodes.LockNotAvailable
            }
          )
            return;
        throw;
      }
      throw new InvalidOperationException("A protected input writer escaped.");
    }
  }

  private static void Require(bool condition, string message)
  {
    if (!condition)
      throw new InvalidOperationException(message);
  }
}
