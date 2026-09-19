using Application.Features.Dispatch.Commands;
using Application.Features.Dispatch.Services;
using Application.Features.Execution.Models;
using Application.Features.Execution.Services;
using Application.Reference;
using Domain.Entities.Execution;
using Domain.Entities.Fleet;
using Microsoft.EntityFrameworkCore;

namespace Server.Tests.Dispatch;

[Trait("Category", "Dispatch")]
[Trait("Kind", "Integration")]
public sealed class InitialExecutionAssignmentTests
{
  [Fact]
  public async Task FirstAssignmentCommitsAcceptedWorkHistoryAndPlanningOnce()
  {
    await using var f = await StopCompletionFixture.CreateAsync();
    var truck = await TruckAsync(f);
    var command = await AssignmentAsync(f, truck.Id);
    var result = await f.CorrectionHandler().Handle(command, default);
    Assert.True(result.Success, string.Join(";", result.Errors ?? []));
    var leg = await f.Db.ExecutionLegs.Include(x => x.Loads).SingleAsync();
    Assert.Equal(truck.Id, leg.TruckId);
    Assert.Equal("planned", leg.Status);
    Assert.Null(leg.StartedAt);
    Assert.Null(leg.CompletedAt);
    Assert.Equal(1, leg.Revision);
    Assert.Equal(f.Actor.Id, leg.RecordedBy);
    Assert.Equal(f.Load.Stops.Select(x => x.Id), leg.Stops.Select(x => x.Id));
    var link = Assert.Single(leg.Loads);
    Assert.Equal(f.Load.Id, link.DispatchId);
    Assert.Equal(f.Load.Stops[0].Id, link.StartVisitId);
    Assert.Equal(f.Load.Stops[^1].Id, link.EndVisitId);
    Assert.All(
      result.Response!.Stops,
      x => Assert.Equal(leg.Id, x.ExecutionLegId)
    );
    var history = await f.Db.ExecutionLegRevisions.SingleAsync();
    Assert.Equal("assignment-accepted", history.Operation);
    Assert.Equal(command.Request.IdempotencyKey, history.CorrelationId);
    Assert.Equal(truck.Id, ExecutionRevisionFacts.Read(history).TruckId);
    var planning = await f.Db.ExecutionPlanningChanges.SingleAsync();
    Assert.Equal(leg.Id, planning.ExecutionLegId);
    Assert.Equal(leg.Revision, planning.AssignmentRevision);
    var retry = await f.CorrectionHandler().Handle(command, default);
    Assert.True(retry.Success);
    Assert.Single(await f.Db.Trips.ToListAsync());
    Assert.Single(await f.Db.ExecutionLegRevisions.ToListAsync());
    Assert.Single(await f.Db.ExecutionPlanningChanges.ToListAsync());
    Assert.Equal(
      result.Response.SourceFingerprint,
      retry.Response!.SourceFingerprint
    );
  }

  [Fact]
  public async Task TruckConfirmationUsesAcceptedExecutionAndCannotResetIt()
  {
    await using var f = await StopCompletionFixture.CreateAsync();
    var truck = await TruckAsync(f);
    var result = await f.AssignmentHandler()
      .Handle(
        new(
          f.Load.Id,
          new(
            truck.UnitNumber,
            f.Load.Stops[0].Id,
            0,
            f.Command(0, null).Update.CompletionIdentity
          )
        ),
        default
      );
    Assert.True(result.Success, string.Join(";", result.Errors ?? []));
    Assert.Equal(truck.Id, (await f.Db.ExecutionLegs.SingleAsync()).TruckId);
    Assert.All(f.Load.Stops, x => Assert.Null(x.TruckId));
    Assert.Equal(
      409,
      (
        await f.AssignmentHandler()
          .Handle(new(f.Load.Id, new(null, null, 1, null)), default)
      ).StatusCode
    );
    Assert.Single(await f.Db.ExecutionLegRevisions.ToListAsync());
  }

  [Fact]
  public async Task CompletionProgressesAndClosesWithoutInventingActualTimes()
  {
    await using var f = await StopCompletionFixture.CreateAsync();
    var truck = await TruckAsync(f);
    Assert.True(
      (
        await f.CorrectionHandler()
          .Handle(await AssignmentAsync(f, truck.Id), default)
      ).Success
    );
    foreach (var stop in f.Load.Stops)
    {
      var state = await DispatchWorkspaceReader.ReadAsync(
        f.Db,
        f.Load.Id,
        true,
        default
      );
      var command = new CorrectDispatchStopCommand(
        f.Load.Id,
        stop.Id,
        new()
        {
          IdempotencyKey = Guid.NewGuid(),
          ExpectedRevision = state!.Response.Revision,
          SourceFingerprint = state.Response.SourceFingerprint,
          Completion = "completed",
        }
      );
      var result = await f.CorrectionHandler().Handle(command, default);
      Assert.True(result.Success, string.Join(";", result.Errors ?? []));
      var current = await f.Db.ExecutionLegs.SingleAsync();
      Assert.Equal(
        stop == f.Load.Stops[^1] ? "completed" : "active",
        current.Status
      );
      Assert.Null(current.StartedAt);
      Assert.Null(current.CompletedAt);
    }
    Assert.Equal(
      f.Load.Stops.Count + 1,
      await f.Db.ExecutionLegRevisions.CountAsync()
    );
    var work = await ExecutionWorkReader.ReadAsync(
      f.Db,
      DateOnly.FromDateTime(f.Clock.GetUtcNow().UtcDateTime),
      new FleetNames(f.Db),
      new ActiveTransfers(f.Db),
      null,
      true,
      true,
      default
    );
    Assert.DoesNotContain(
      work.SelectMany(x => x.Loads),
      x => x.Id == f.Load.Id
    );
  }

  [Fact]
  public async Task AmbiguousResourcesRetainEveryStopAndRequireReview()
  {
    await using var f = await StopCompletionFixture.CreateAsync();
    var truck = await TruckAsync(f);
    var driver = new Driver { Id = Guid.NewGuid(), Name = "Later driver" };
    f.Db.Drivers.Add(driver);
    f.Load.Stops[^1].DriverId = driver.Id;
    await f.Db.SaveChangesAsync();
    var command = await AssignmentAsync(f, truck.Id);
    command.Request.ChangeDriver = false;
    var result = await f.CorrectionHandler().Handle(command, default);
    Assert.True(result.Success);
    Assert.Contains(
      "explicit execution boundaries",
      result.Response!.SourceReviewReason
    );
    Assert.Null(f.Load.Stops[0].DriverId);
    Assert.Equal(driver.Id, f.Load.Stops[^1].DriverId);
    Assert.False(await f.Db.ExecutionLegs.AnyAsync());
    Assert.False(await f.Db.ExecutionLegRevisions.AnyAsync());
    Assert.False(await f.Db.ExecutionPlanningChanges.AnyAsync());
    (await f.Db.DispatchWorkspaces.SingleAsync()).SourceReviewReason = null;
    await f.Db.SaveChangesAsync();
    var refreshed = await DispatchWorkspaceReader.ReadAsync(
      f.Db,
      f.Load.Id,
      true,
      default
    );
    Assert.Contains(
      "Initial execution is not accepted",
      refreshed!.Response.SourceReviewReason
    );
    var resolved = await f.CorrectionHandler()
      .Handle(await AssignmentAsync(f, truck.Id), default);
    Assert.True(resolved.Success);
    Assert.Null(resolved.Response!.SourceReviewReason);
    Assert.Single(await f.Db.ExecutionLegs.ToListAsync());
  }

  [Fact]
  public async Task ActiveResourceConflictRollsBackInitialAcceptanceAndReceipt()
  {
    await using var f = await StopCompletionFixture.CreateAsync();
    var truck = await TruckAsync(f);
    var active = new ExecutionLeg
    {
      Id = Guid.NewGuid(),
      Trip = new() { Id = Guid.NewGuid() },
      TruckId = truck.Id,
      Status = "active",
    };
    f.Db.ExecutionLegs.Add(active);
    await f.Db.SaveChangesAsync();
    var command = await AssignmentAsync(f, truck.Id);
    command.Request.Completion = "completed";
    Assert.Equal(
      409,
      (await f.CorrectionHandler().Handle(command, default)).StatusCode
    );
    f.Db.ChangeTracker.Clear();
    Assert.Single(await f.Db.ExecutionLegs.ToListAsync());
    Assert.False(await f.Db.LoadExecutionLegs.AnyAsync());
    Assert.False(await f.Db.ExecutionLegRevisions.AnyAsync());
    Assert.False(await f.Db.ExecutionPlanningChanges.AnyAsync());
    Assert.False(await f.Db.DispatchWorkspaceRevisions.AnyAsync());
    var stop = await f.Db.DispatchStops.SingleAsync(x =>
      x.Id == command.StopId
    );
    Assert.Null(stop.TruckId);
    Assert.False(stop.IsCompleted);
  }

  [Fact]
  public async Task LegacyOperationUpdatesAcceptedRowsAndRejectsStaleIdentity()
  {
    await using var f = await StopCompletionFixture.CreateAsync();
    var truck = await TruckAsync(f);
    Assert.True(
      (
        await f.CorrectionHandler()
          .Handle(await AssignmentAsync(f, truck.Id), default)
      ).Success
    );
    var state = await DispatchWorkspaceReader.ReadAsync(
      f.Db,
      f.Load.Id,
      true,
      default
    );
    var stop = state!.Response.Load.Stops[0];
    var command = new SetStopOperationCommand(
      f.Load.Id,
      stop.Id,
      new("Waypoint", "Empty", stop.OperationRevision, stop.CompletionIdentity)
    );
    var result = await f.OperationHandler().Handle(command, default);
    Assert.True(result.Success, string.Join(";", result.Errors ?? []));
    var leg = await f.Db.ExecutionLegs.SingleAsync();
    Assert.Equal("Waypoint", leg.Stops.Single(x => x.Id == stop.Id).Job);
    Assert.Equal(2, leg.Revision);
    Assert.Equal(2, await f.Db.ExecutionLegRevisions.CountAsync());
    Assert.Equal(2, await f.Db.ExecutionPlanningChanges.CountAsync());
    Assert.Equal(
      409,
      (await f.OperationHandler().Handle(command, default)).StatusCode
    );
  }

  [Fact]
  public async Task TransferUsesInitialLegAndLegacyActionsCannotConfirmHandoff()
  {
    await using var f = await StopCompletionFixture.CreateAsync();
    var truck = await TruckAsync(f);
    var incoming = new Truck
    {
      Id = Guid.NewGuid(),
      UnitNumber = "Incoming",
      ExternalId = "incoming",
      IsActive = true,
    };
    f.Db.Trucks.Add(incoming);
    await f.Db.SaveChangesAsync();
    Assert.True(
      (
        await f.CorrectionHandler()
          .Handle(await AssignmentAsync(f, truck.Id), default)
      ).Success
    );
    var leg = await f.Db.ExecutionLegs.SingleAsync();
    var transfer = new PlanSwitchRequest(
      Guid.NewGuid(),
      "Transfer yard",
      null,
      [
        new(
          f.Load.Id,
          null,
          null,
          ExecutionSnapshots.Fingerprint(f.Load),
          new(truck.Id, null, null),
          new(incoming.Id, null, null)
        )
        {
          OutgoingLegId = leg.Id,
          ExpectedOutgoingRevision = leg.Revision,
          SplitAfterVisitId = f.Load.Stops[0].Id,
        },
      ]
    )
    {
      Latitude = 43.6m,
      Longitude = -79.3m,
    };
    var planned = await f.SwitchPlanner().Handle(new(transfer), default);
    Assert.True(planned.Success, string.Join(";", planned.Errors ?? []));
    var pair = Assert.Single(planned.Response!.Legs);
    Assert.Equal(leg.Id, pair.OutgoingLegId);
    Assert.Equal(2, await f.Db.ExecutionLegs.CountAsync());
    var state = (
      await DispatchWorkspaceReader.ReadAsync(f.Db, f.Load.Id, true, default)
    )!;
    var release = state.Response.Load.Stops.Single(x =>
      x.Id == pair.ReleaseVisitId
    );
    Assert.Equal(
      409,
      (
        await f.Handler()
          .Handle(
            new(
              f.Load.Id,
              release.Id,
              new(
                f.Clock.GetUtcNow().AddMinutes(-1),
                release.ManualCompletionRevision,
                release.CompletionIdentity
              )
            ),
            default
          )
      ).StatusCode
    );
    Assert.Equal(
      409,
      (
        await f.OperationHandler()
          .Handle(
            new(
              f.Load.Id,
              release.Id,
              new(
                "Waypoint",
                "Empty",
                release.OperationRevision,
                release.CompletionIdentity
              )
            ),
            default
          )
      ).StatusCode
    );
    Assert.Null((await f.Db.SwitchParticipants.SingleAsync()).ReleasedBy);
    var future = state.Response.Stops.Last();
    Assert.Equal(
      409,
      (
        await f.CorrectionHandler()
          .Handle(
            new(
              f.Load.Id,
              future.Id,
              new()
              {
                IdempotencyKey = Guid.NewGuid(),
                ExpectedRevision = state.Response.Revision,
                SourceFingerprint = state.Response.SourceFingerprint,
                Completion = "completed",
              }
            ),
            default
          )
      ).StatusCode
    );
    await CompleteAsync(f, f.Load.Stops[0].Id);
    Assert.Equal("active", leg.Status);
    var participant = await f
      .Db.SwitchParticipants.Include(x => x.Switch)
      .SingleAsync();
    var released = await f.SwitchRelease()
      .Handle(
        new(
          new(
            Guid.NewGuid(),
            participant.SwitchId,
            participant.Id,
            participant.Switch.Revision,
            participant.Revision,
            leg.Revision,
            null
          )
        ),
        default
      );
    Assert.True(released.Success, string.Join(";", released.Errors ?? []));
    var incomingLeg = await f.Db.ExecutionLegs.SingleAsync(x =>
      x.Id == pair.IncomingLegId
    );
    var received = await f.SwitchReceipt()
      .Handle(
        new(
          new(
            Guid.NewGuid(),
            participant.SwitchId,
            participant.Id,
            participant.Switch.Revision,
            participant.Revision,
            incomingLeg.Revision,
            null
          )
        ),
        default
      );
    Assert.True(received.Success, string.Join(";", received.Errors ?? []));
    foreach (var stop in f.Load.Stops.Skip(1))
      await CompleteAsync(f, stop.Id);
    Assert.Equal("completed", incomingLeg.Status);
    Assert.Null(incomingLeg.CompletedAt);
    var receiptStop = incomingLeg.Stops.Single(x =>
      x.Id == pair.ReceiveVisitId
    );
    Assert.False(receiptStop.ExecutionCompleted);
    Assert.Null(receiptStop.ManualCompletedBy);
    Assert.Equal(f.Actor.Id, participant.ReceivedBy);
  }

  [Fact]
  public async Task HeaderResourcesRemainAssignedWhenStopsDoNotRepeatThem()
  {
    await using var f = await StopCompletionFixture.CreateAsync();
    var truck = await TruckAsync(f);
    var driver = new Driver { Id = Guid.NewGuid(), Name = "Header driver" };
    f.Db.Drivers.Add(driver);
    f.Load.DriverId = driver.Id;
    f.Load.DriverName = driver.Name;
    await f.Db.SaveChangesAsync();
    var command = await AssignmentAsync(f, truck.Id);
    command.Request.ChangeDriver = false;
    var result = await f.CorrectionHandler().Handle(command, default);
    Assert.True(result.Success, string.Join(";", result.Errors ?? []));
    Assert.Equal(driver.Id, (await f.Db.ExecutionLegs.SingleAsync()).DriverId);
  }

  [Theory]
  [InlineData("future")]
  [InlineData("reversed")]
  public async Task InvalidSourceActualsRemainForReviewWithoutAcceptedHistory(
    string kind
  )
  {
    await using var f = await StopCompletionFixture.CreateAsync();
    var truck = await TruckAsync(f);
    f.Load.Stops[0].ArrivedAt = f.Clock.GetUtcNow().UtcDateTime.AddMinutes(-1);
    f.Load.Stops[0].PickedUpAt = f
      .Clock.GetUtcNow()
      .UtcDateTime.AddMinutes(kind == "future" ? 1 : -2);
    await f.Db.SaveChangesAsync();
    var result = await f.CorrectionHandler()
      .Handle(await AssignmentAsync(f, truck.Id), default);
    Assert.True(result.Success);
    Assert.Contains(
      "Review actual visit times",
      result.Response!.SourceReviewReason
    );
    Assert.False(await f.Db.ExecutionLegs.AnyAsync());
    Assert.NotNull(f.Load.Stops[0].PickedUpAt);
  }

  private static async Task<Truck> TruckAsync(StopCompletionFixture f)
  {
    var truck = new Truck
    {
      Id = Guid.NewGuid(),
      UnitNumber = "Initial",
      ExternalId = "initial",
      IsActive = true,
    };
    f.Db.Trucks.Add(truck);
    await f.Db.SaveChangesAsync();
    return truck;
  }

  private static async Task CompleteAsync(StopCompletionFixture f, Guid stopId)
  {
    var state = (
      await DispatchWorkspaceReader.ReadAsync(f.Db, f.Load.Id, true, default)
    )!;
    var result = await f.CorrectionHandler()
      .Handle(
        new(
          f.Load.Id,
          stopId,
          new()
          {
            IdempotencyKey = Guid.NewGuid(),
            ExpectedRevision = state.Response.Revision,
            SourceFingerprint = state.Response.SourceFingerprint,
            Completion = "completed",
          }
        ),
        default
      );
    Assert.True(result.Success, string.Join(";", result.Errors ?? []));
  }

  private static async Task<CorrectDispatchStopCommand> AssignmentAsync(
    StopCompletionFixture f,
    Guid truck
  )
  {
    var state = await DispatchWorkspaceReader.ReadAsync(
      f.Db,
      f.Load.Id,
      true,
      default
    );
    return new(
      f.Load.Id,
      f.Load.Stops[0].Id,
      new()
      {
        IdempotencyKey = Guid.NewGuid(),
        ExpectedRevision = state!.Response.Revision,
        SourceFingerprint = state.Response.SourceFingerprint,
        Completion = "keep",
        ChangeAssignment = true,
        TruckId = truck,
      }
    );
  }
}
