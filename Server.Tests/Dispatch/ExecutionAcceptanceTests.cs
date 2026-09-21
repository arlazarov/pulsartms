using Application.Features.Execution.Commands;
using Application.Features.Execution.Models;
using Application.Features.Execution.Services;
using Domain.Entities.Dispatch;
using Domain.Entities.Execution;
using Domain.Entities.Fleet;
using Domain.Entities.Mileage;
using Domain.Models.Execution;
using Microsoft.EntityFrameworkCore;

namespace Server.Tests.Dispatch;

[Trait("Category", "Dispatch")]
[Trait("Kind", "Integration")]
public sealed class ExecutionAcceptanceTests
{
  [Theory]
  [InlineData(true)]
  [InlineData(false)]
  public async Task TransferSplitUsesTheSameMileageProtectionAsStopAcceptance(
    bool actual
  )
  {
    await using var f = await StopCompletionFixture.CreateAsync();
    var leg = await SeedAsync(f);
    var stops = ExecutionStopRows.Read(leg);
    var changed = Movement(leg, stops[1], stops[2]);
    var retained = Movement(leg, stops[0], stops[1]);
    if (actual)
      changed.ActualMiles = 12;
    var outgoing = await f.Db.Trucks.SingleAsync(x => x.Id == leg.TruckId);
    outgoing.IsActive = true;
    var incoming = new Truck
    {
      Id = Guid.NewGuid(),
      UnitNumber = "202",
      ExternalId = "incoming",
      IsActive = true,
    };
    f.Db.Trucks.Add(incoming);
    f.Db.Movements.AddRange(changed, retained);
    await f.Db.SaveChangesAsync();
    var command = new PlanSwitchCommand(
      new(
        Guid.NewGuid(),
        "Transfer yard",
        null,
        [
          new(
            f.Load.Id,
            null,
            null,
            ExecutionSnapshots.Fingerprint(f.Load),
            new(leg.TruckId, null, null),
            new(incoming.Id, null, null)
          )
          {
            OutgoingLegId = leg.Id,
            ExpectedOutgoingRevision = leg.Revision,
            SplitAfterVisitId = stops[1].Id,
          },
        ]
      )
      {
        Latitude = 35,
        Longitude = -80,
      }
    );

    var result = await f.SwitchPlanner().Handle(command, default);

    Assert.Equal(!actual, result.Success);
    f.Db.ChangeTracker.Clear();
    var saved = await f.Db.ExecutionLegs.SingleAsync(x => x.Id == leg.Id);
    Assert.Equal(actual ? 1 : 2, saved.Revision);
    Assert.Equal(actual ? stops.Count : 3, saved.Stops.Count);
    Assert.Equal(
      !actual,
      (
        await f.Db.Movements.SingleAsync(x => x.Id == changed.Id)
      ).PlannedSuperseded
    );
    Assert.False(
      (
        await f.Db.Movements.SingleAsync(x => x.Id == retained.Id)
      ).PlannedSuperseded
    );
    Assert.Equal(actual ? 0 : 2, await f.Db.ExecutionLegRevisions.CountAsync());
    Assert.Equal(
      actual ? 0 : 2,
      await f.Db.ExecutionPlanningChanges.CountAsync()
    );
    Assert.Equal(actual ? 1 : 2, await f.Db.ExecutionLegs.CountAsync());
    if (!actual)
    {
      var created = await f.Db.ExecutionLegRevisions.SingleAsync(x =>
        x.ExecutionLegId != leg.Id
      );
      Assert.Equal(1, created.Revision);
    }
  }

  [Fact]
  public async Task ImportInsertsVisitAndRetiresObsoleteMileageAtomicallyOnce()
  {
    await using var f = await StopCompletionFixture.CreateAsync();
    var leg = await SeedAsync(f);
    var oldStops = ExecutionStopRows.Read(leg);
    var obsolete = Movement(leg, oldStops[0], oldStops[1]);
    var unaffected = Movement(leg, oldStops[3], oldStops[4]);
    f.Db.Movements.AddRange(obsolete, unaffected);
    await f.Db.SaveChangesAsync();
    AddSourceStop(f, 1);
    var now = f.Clock.GetUtcNow().UtcDateTime;
    await using var tx = await f.Db.Database.BeginTransactionAsync();

    var changes = await ExecutionSourceReconciliation.ApplyAsync(
      f.Db,
      [f.Load],
      now,
      default
    );
    await f.Db.SaveChangesAsync();
    Assert.Same(leg, Assert.Single(changes));
    Assert.Equal(2, leg.Revision);
    Assert.Equal(6, leg.Stops.Count);
    Assert.True(obsolete.PlannedSuperseded);
    Assert.False(unaffected.PlannedSuperseded);
    var allocation = await f.Db.MovementAllocationEvents.SingleAsync();
    Assert.Equal(obsolete.Id, allocation.MovementId);
    Assert.Equal("planned-segment-superseded", allocation.Reason);
    Assert.Equal(Guid.Empty, allocation.RecordedBy);
    var history = await f.Db.ExecutionLegRevisions.SingleAsync();
    Assert.Null(history.RecordedBy);
    Assert.Equal(6, ExecutionRevisionFacts.Read(history).Stops.Length);
    var work = await f.Db.ExecutionPlanningChanges.SingleAsync();
    Assert.Equal(leg.Revision, work.AssignmentRevision);

    Assert.Empty(
      await ExecutionSourceReconciliation.ApplyAsync(
        f.Db,
        [f.Load],
        now,
        default
      )
    );
    await f.Db.SaveChangesAsync();
    Assert.Equal(1, await f.Db.ExecutionPlanningChanges.CountAsync());
    Assert.Equal(1, await f.Db.ExecutionLegRevisions.CountAsync());
    Assert.Equal(1, await f.Db.MovementAllocationEvents.CountAsync());
    await tx.CommitAsync();
  }

  [Theory]
  [InlineData("started")]
  [InlineData("actual")]
  [InlineData("manual")]
  [InlineData("distance-decision")]
  [InlineData("allocation-decision")]
  public async Task ImportCannotInsertAcrossProtectedMileage(string evidence)
  {
    await using var f = await StopCompletionFixture.CreateAsync();
    var leg = await SeedAsync(f);
    var before = ExecutionStopRows.Read(leg);
    var movement = Movement(leg, before[0], before[1]);
    if (evidence == "started")
      movement.StartedAt = f.Clock.GetUtcNow().UtcDateTime;
    if (evidence == "actual")
      movement.ActualMiles = 10;
    if (evidence == "manual")
      movement.ManualOverride = true;
    f.Db.Movements.Add(movement);
    await f.Db.SaveChangesAsync();
    if (evidence == "distance-decision")
      f.Db.MovementDistanceEvidence.Add(
        new()
        {
          Id = Guid.NewGuid(),
          MovementId = movement.Id,
          Revision = 1,
          Basis = "planned",
          Source = "manual",
          Miles = 10,
          RecordedBy = f.Actor.Id,
        }
      );
    if (evidence == "allocation-decision")
      f.Db.MovementAllocationEvents.Add(
        new()
        {
          Id = Guid.NewGuid(),
          MovementId = movement.Id,
          Revision = 1,
          RecordedBy = f.Actor.Id,
          Reason = "manual-selection",
        }
      );
    await f.Db.SaveChangesAsync();
    AddSourceStop(f, 1);
    await using var tx = await f.Db.Database.BeginTransactionAsync();
    var now = f.Clock.GetUtcNow().UtcDateTime;

    await ExecutionSourceReconciliation.ApplyAsync(
      f.Db,
      [f.Load],
      now,
      default
    );
    await f.Db.SaveChangesAsync();

    Assert.Equal(before.Select(x => x.Id), leg.Stops.Select(x => x.Id));
    Assert.Contains("recorded mileage", leg.SourceReviewReason);
    Assert.False(movement.PlannedSuperseded);
    Assert.Equal(1, leg.Revision);
    Assert.Equal(0, await f.Db.ExecutionLegRevisions.CountAsync());
    Assert.Empty(
      await ExecutionSourceReconciliation.ApplyAsync(
        f.Db,
        [f.Load],
        now,
        default
      )
    );
    await tx.CommitAsync();
  }

  [Fact]
  public async Task ImportCanExtendFuturePathAfterAnObservedNeighbour()
  {
    await using var f = await StopCompletionFixture.CreateAsync();
    var leg = await SeedAsync(f);
    var before = ExecutionStopRows.Read(leg);
    var observed = Movement(leg, before[0], before[1]);
    observed.ActualMiles = 10;
    var obsolete = Movement(leg, before[1], before[2]);
    f.Db.Movements.AddRange(observed, obsolete);
    await f.Db.SaveChangesAsync();
    AddSourceStop(f, 2);
    await using var tx = await f.Db.Database.BeginTransactionAsync();

    await ExecutionSourceReconciliation.ApplyAsync(
      f.Db,
      [f.Load],
      f.Clock.GetUtcNow().UtcDateTime,
      default
    );
    await f.Db.SaveChangesAsync();

    Assert.Equal(6, leg.Stops.Count);
    Assert.Null(leg.SourceReviewReason);
    Assert.True(obsolete.PlannedSuperseded);
    Assert.False(observed.PlannedSuperseded);
    Assert.Equal(10, observed.ActualMiles);
    Assert.Equal(1, observed.Revision);
    await tx.CommitAsync();
  }

  [Fact]
  public async Task RollbackRestoresStopsMileageHistoryAndQueueTogether()
  {
    await using var f = await StopCompletionFixture.CreateAsync();
    var leg = await SeedAsync(f);
    var before = ExecutionStopRows.Read(leg);
    var movement = Movement(leg, before[0], before[1]);
    f.Db.Movements.Add(movement);
    await f.Db.SaveChangesAsync();
    await using (var tx = await f.Db.Database.BeginTransactionAsync())
    {
      var after = before.Select(ExecutionSnapshots.Copy).ToList();
      after[0].Address = "Corrected facility";
      await ExecutionAcceptance.ApplyAsync(
        f.Db,
        [new(leg, after)],
        "workspace-updated",
        f.Actor.Id,
        Guid.NewGuid(),
        f.Clock.GetUtcNow().UtcDateTime,
        default
      );
      await f.Db.SaveChangesAsync();
      Assert.True(movement.PlannedSuperseded);
      await tx.RollbackAsync();
    }
    f.Db.ChangeTracker.Clear();

    var saved = await f.Db.ExecutionLegs.SingleAsync();
    Assert.Equal(1, saved.Revision);
    Assert.Equal(before[0].Address, saved.Stops[0].Address);
    Assert.False((await f.Db.Movements.SingleAsync()).PlannedSuperseded);
    Assert.Equal(0, await f.Db.ExecutionPlanningChanges.CountAsync());
    Assert.Equal(0, await f.Db.ExecutionLegRevisions.CountAsync());
    Assert.Equal(0, await f.Db.MovementAllocationEvents.CountAsync());
  }

  [Fact]
  public async Task ManualAcceptancePreservesActorAndValidatesBeforeMutation()
  {
    await using var f = await StopCompletionFixture.CreateAsync();
    var leg = await SeedAsync(f);
    var after = ExecutionStopRows.Read(leg);
    after[0].Address = "Corrected facility";
    var key = Guid.NewGuid();
    await Assert.ThrowsAsync<InvalidOperationException>(() => Accept());
    Assert.Equal(1, leg.Revision);
    Assert.NotEqual(after[0].Address, leg.Stops[0].Address);
    await using var tx = await f.Db.Database.BeginTransactionAsync();
    await Accept();
    await f.Db.SaveChangesAsync();

    var history = await f.Db.ExecutionLegRevisions.SingleAsync();
    Assert.Equal(f.Actor.Id, history.RecordedBy);
    Assert.Equal(key, history.CorrelationId);
    Assert.Equal(
      after[0].Address,
      ExecutionRevisionFacts.Read(history).Stops[0].Address
    );
    Assert.Equal(
      2,
      (await f.Db.ExecutionPlanningChanges.SingleAsync()).AssignmentRevision
    );
    await tx.CommitAsync();

    Task Accept() =>
      ExecutionAcceptance.ApplyAsync(
        f.Db,
        [new(leg, after)],
        "workspace-updated",
        f.Actor.Id,
        key,
        f.Clock.GetUtcNow().UtcDateTime,
        default
      );
  }

  [Fact]
  public async Task NewMileageBetweenPreviewAndAcceptanceRejectsTheChange()
  {
    await using var f = await StopCompletionFixture.CreateAsync();
    var leg = await SeedAsync(f);
    var before = ExecutionStopRows.Read(leg);
    var after = before.Select(ExecutionSnapshots.Copy).ToList();
    after[0].Address = "Corrected facility";
    Assert.False(
      await ExecutionAcceptance.HasProtectedPathAsync(f.Db, leg, after, default)
    );
    var movement = Movement(leg, before[0], before[1]);
    movement.ActualMiles = 12;
    f.Db.Movements.Add(movement);
    await f.Db.SaveChangesAsync();
    await using var tx = await f.Db.Database.BeginTransactionAsync();

    await Assert.ThrowsAsync<DbUpdateConcurrencyException>(
      () =>
        ExecutionAcceptance.ApplyAsync(
          f.Db,
          [new(leg, after)],
          "workspace-updated",
          f.Actor.Id,
          Guid.NewGuid(),
          f.Clock.GetUtcNow().UtcDateTime,
          default
        )
    );

    Assert.Equal(1, leg.Revision);
    Assert.Equal(before[0].Address, leg.Stops[0].Address);
    Assert.Empty(f.Db.ExecutionLegRevisions.Local);
    Assert.Empty(f.Db.ExecutionPlanningChanges.Local);
  }

  private static async Task<ExecutionLeg> SeedAsync(StopCompletionFixture f)
  {
    var truck = new Truck
    {
      Id = Guid.NewGuid(),
      ExternalId = "acceptance-truck",
      UnitNumber = "201",
    };
    var trip = new Trip { Id = Guid.NewGuid() };
    var leg = new ExecutionLeg
    {
      Id = Guid.NewGuid(),
      Trip = trip,
      TripId = trip.Id,
      TruckId = truck.Id,
      Status = "active",
      Revision = 1,
      RecordedAt = f.Clock.GetUtcNow().UtcDateTime,
      SourceSignature = ExecutionSnapshots.Fingerprint(f.Load),
    };
    ExecutionStopRows.Replace(leg, f.Load.Stops);
    leg.Loads.Add(
      new()
      {
        Id = Guid.NewGuid(),
        DispatchId = f.Load.Id,
        ExecutionLegId = leg.Id,
        StartVisitId = f.Load.Stops[0].Id,
        EndVisitId = f.Load.Stops[^1].Id,
      }
    );
    f.Db.Trucks.Add(truck);
    f.Db.ExecutionLegs.Add(leg);
    await f.Db.SaveChangesAsync();
    return leg;
  }

  private static void AddSourceStop(StopCompletionFixture f, int position)
  {
    var stop = new DispatchStop
    {
      Id = Guid.NewGuid(),
      DispatchId = f.Load.Id,
      Job = "Waypoint",
      Address = "New future facility",
    };
    f.Load.Stops.Insert(position, stop);
    for (var i = 0; i < f.Load.Stops.Count; i++)
      f.Load.Stops[i].Sequence = i + 1;
    f.Db.DispatchStops.Add(stop);
  }

  private static Movement Movement(
    ExecutionLeg leg,
    DispatchStop from,
    DispatchStop to
  ) =>
    new()
    {
      Id = Guid.NewGuid(),
      IdempotencyKey = Guid.NewGuid(),
      TruckId = leg.TruckId,
      ExecutionLegId = leg.Id,
      FromVisitId = from.Id,
      ToVisitId = to.Id,
      Origin = "native-route",
      Revision = 1,
      PlannedMiles = 10,
    };
}
