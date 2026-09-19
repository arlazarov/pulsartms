using System.Data;
using Application.Caching;
using Application.Features.Dispatch.Commands;
using Application.Features.Dispatch.Services;
using Application.Features.Execution.Models;
using Application.Features.Execution.Services;
using Application.Features.Routing.Background;
using Application.Features.Routing.Options;
using Application.Features.Synchronization.Options;
using Application.Interfaces;
using Domain.Entities.Fleet;
using Infrastructure.Identity;
using Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace CoreMigrationProbe;

internal static class InitialAssignmentProbe
{
  public static async Task VerifyAsync(AppDbContext db)
  {
    var actor = await db.Users.SingleAsync();
    var truck = new Truck
    {
      Id = Guid.NewGuid(),
      ExternalId = "initial-assignment-fixture",
      UnitNumber = "initial-assignment-fixture",
      IsActive = true,
    };
    db.Trucks.Add(truck);
    await db.SaveChangesAsync();
    using var reads = new ReadCache(
      Options.Create(new SynchronizationOptions())
    );
    var queue = new RoutePreparationQueue(
      Options.Create(new RoutePreparationOptions()),
      TimeProvider.System
    );
    var creator = new CreateDispatchHandler(
      db,
      new Caller(actor.IdentityUserId),
      new UserRoleService(db),
      TimeProvider.System,
      reads,
      queue
    );
    var created = await creator.Handle(
      new(DispatchIndependenceProbe.Request()),
      default
    );
    var competing = await creator.Handle(
      new(DispatchIndependenceProbe.Request()),
      default
    );
    Require(
      created.Success && competing.Success,
      "Initial-assignment loads must be created."
    );
    var loadId = created.Response!.Load.Id;
    var concurrentLoadId = competing.Response!.Load.Id;
    var originalTripCount = await db.Trips.CountAsync();
    var handler = new CorrectDispatchStopHandler(
      db,
      new Caller(actor.IdentityUserId),
      new UserRoleService(db),
      TimeProvider.System,
      reads,
      queue
    );
    var state = (
      await DispatchWorkspaceReader.ReadAsync(db, loadId, true, default)
    )!;
    var command = new CorrectDispatchStopCommand(
      loadId,
      state.Response.Stops[0].Id,
      new()
      {
        IdempotencyKey = Guid.NewGuid(),
        ExpectedRevision = state.Response.Revision,
        SourceFingerprint = state.Response.SourceFingerprint,
        Completion = "keep",
        ChangeAssignment = true,
        TruckId = truck.Id,
      }
    );
    var first = await handler.Handle(command, default);
    Require(first.Success, "Initial assignment must commit on PostgreSQL.");
    db.ChangeTracker.Clear();
    var retry = await handler.Handle(command, default);
    Require(
      retry.Success
        && first.Response!.SourceFingerprint
          == retry.Response!.SourceFingerprint,
      "Assignment replay must survive a fresh change tracker."
    );
    Require(
      await db.ExecutionLegs.CountAsync(x => x.TruckId == truck.Id) == 1
        && await db.ExecutionLegRevisions.CountAsync(x => x.TruckId == truck.Id)
          == 1
        && await db.ExecutionPlanningChanges.CountAsync(x =>
          x.TruckId == truck.Id
        ) == 1,
      "Initial assignment and replay must retain one leg, history and planning request."
    );

    var options = new DbContextOptionsBuilder<AppDbContext>()
      .UseNpgsql(db.Database.GetConnectionString())
      .Options;
    await using var left = new AppDbContext(options);
    await using var right = new AppDbContext(options);
    await using var leftTx = await left.Database.BeginTransactionAsync(
      IsolationLevel.Serializable
    );
    await using var rightTx = await right.Database.BeginTransactionAsync(
      IsolationLevel.Serializable
    );
    foreach (var context in new[] { left, right })
    {
      var load = await context
        .Dispatches.Include(x => x.Stops)
        .SingleAsync(x => x.Id == concurrentLoadId);
      var stops = load
        .Stops.OrderBy(x => x.Sequence)
        .Select(ExecutionSnapshots.Copy)
        .ToArray();
      foreach (var stop in stops)
        stop.TruckId = truck.Id;
      await InitialExecutionAssignment.AcceptAsync(
        context,
        load,
        stops,
        actor.Id,
        null,
        DateTime.UtcNow,
        default
      );
    }
    await left.SaveChangesAsync();
    await leftTx.CommitAsync();
    var rejected = false;
    try
    {
      await right.SaveChangesAsync();
      await rightTx.CommitAsync();
    }
    catch (Exception ex) when (right.IsWriteConflict(ex))
    {
      rejected = true;
      await rightTx.RollbackAsync();
    }
    Require(
      rejected
        && await db.LoadExecutionLegs.CountAsync(x =>
          x.DispatchId == concurrentLoadId
        ) == 1,
      "Independent transactions cannot accept two first assignments."
    );
    Require(
      await db.Trips.CountAsync() == originalTripCount + 2
        && await db.ExecutionLegRevisions.CountAsync(x => x.TruckId == truck.Id)
          == 2
        && await db.ExecutionPlanningChanges.CountAsync(x =>
          x.TruckId == truck.Id
        ) == 2,
      "A losing transaction cannot retain an orphan trip, history or planning request."
    );

    Console.WriteLine(
      "Initial assignment, persisted replay and competing transaction rollback passed."
    );
  }

  private sealed class Caller(string identity) : ICurrentUser
  {
    public bool IsAuthenticated => true;
    public string IdentityUserId => identity;
  }

  private static void Require(bool condition, string message)
  {
    if (!condition)
      throw new InvalidOperationException(message);
  }
}
