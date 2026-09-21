using Application.Features.Dispatch.Commands;
using Application.Features.Dispatch.Services;
using Application.Features.Execution.Models;
using Domain.Entities.Execution;
using Domain.Entities.Fleet;
using Domain.Models.Execution;
using Microsoft.EntityFrameworkCore;

namespace Server.Tests.Dispatch;

[Trait("Category", "Dispatch")]
[Trait("Kind", "Integration")]
public sealed class TransferCorrectionTests
{
  [Fact]
  public async Task ConfirmedDropTruckIsDirectlyCorrectableWithoutChangingHookOrActuals()
  {
    await using var f = await StopCompletionFixture.CreateAsync();
    var transfer = await Seed(f);
    var command = await Command(f, transfer.Participant.ReleaseVisitId);
    var stale = await Command(f, transfer.Participant.ReleaseVisitId);
    command.Request.ChangeTruck = true;
    command.Request.TruckId = transfer.ReplacementTruck.Id;
    stale.Request.ChangeTruck = true;
    stale.Request.TruckId = transfer.ReplacementTruck.Id;
    var result = await f.CorrectionHandler().Handle(command, default);
    Assert.True(result.Success, string.Join(";", result.Errors ?? []));
    Assert.Equal(transfer.ReplacementTruck.Id, transfer.Outgoing.TruckId);
    Assert.NotEqual(transfer.ReplacementTruck.Id, transfer.Incoming.TruckId);
    Assert.Equal(transfer.Released, transfer.Participant.ReleasedAt);
    Assert.Equal(transfer.Received, transfer.Participant.ReceivedAt);
    Assert.Equal(transfer.Actor, transfer.Participant.ReleasedBy);
    Assert.Equal("completed", transfer.Outgoing.Status);
    Assert.Equal("active", transfer.Incoming.Status);
    Assert.Equal(
      "confirmed",
      result
        .Response!.Stops.Single(x => x.Id == command.StopId)
        .Transfer!.Status
    );
    Assert.Equal(
      409,
      (await f.CorrectionHandler().Handle(stale, default)).StatusCode
    );
    Assert.True((await f.CorrectionHandler().Handle(command, default)).Success);
    Assert.Equal(1, await f.Db.DispatchWorkspaceRevisions.CountAsync());
  }

  [Fact]
  public async Task HookTrailerCorrectionUpdatesBothAssignmentsAndCustodyInOneAudit()
  {
    await using var f = await StopCompletionFixture.CreateAsync();
    var transfer = await Seed(f);
    var command = await Command(f, transfer.Participant.ReceiveVisitId);
    command.Request.ChangeTrailer = true;
    command.Request.TrailerId = transfer.ReplacementTrailer.Id;
    var result = await f.CorrectionHandler().Handle(command, default);
    Assert.True(result.Success, string.Join(";", result.Errors ?? []));
    Assert.Equal(transfer.ReplacementTrailer.Id, transfer.Outgoing.TrailerId);
    Assert.Equal(transfer.ReplacementTrailer.Id, transfer.Incoming.TrailerId);
    var custody = await f.Db.TrailerCustodyIntervals.SingleAsync();
    Assert.Equal(transfer.ReplacementTrailer.Id, custody.TrailerId);
    Assert.Equal(transfer.Released, custody.ReleasedAt);
    Assert.Equal(transfer.Received, custody.ReceivedAt);
    Assert.Equal(transfer.Actor, custody.ReleasedBy);
    Assert.Equal(transfer.Actor, custody.ReceivedBy);
    Assert.Equal(2, custody.Revision);
    Assert.Equal(2, transfer.Participant.Revision);
    Assert.Equal(2, transfer.Participant.Switch.Revision);
    var audit = await f.Db.DispatchWorkspaceRevisions.SingleAsync();
    Assert.Contains("Old trailer", audit.BeforeJson);
    Assert.Contains("New trailer", audit.SnapshotJson);
    Assert.Equal(
      2,
      result.Response!.Stops.Count(x =>
        x.Transfer?.IncomingTrailerNumber == "New trailer"
      )
    );
  }

  [Theory]
  [InlineData("pending")]
  [InlineData("completed")]
  public async Task ResourceCorrectionCannotUndoOrConfirmActualTransfer(
    string completion
  )
  {
    await using var f = await StopCompletionFixture.CreateAsync();
    var transfer = await Seed(f);
    var command = await Command(f, transfer.Participant.ReleaseVisitId);
    command.Request.Completion = completion;
    command.Request.ChangeTruck = true;
    command.Request.TruckId = transfer.ReplacementTruck.Id;
    var result = await f.CorrectionHandler().Handle(command, default);
    Assert.Equal(400, result.StatusCode);
    Assert.Equal(transfer.Released, transfer.Participant.ReleasedAt);
    Assert.Equal(0, await f.Db.DispatchWorkspaceRevisions.CountAsync());
  }

  [Fact]
  public async Task ConflictingActiveTruckIsRejectedWithoutChangingTransfer()
  {
    await using var f = await StopCompletionFixture.CreateAsync();
    var transfer = await Seed(f);
    f.Db.ExecutionLegs.Add(
      new()
      {
        Id = Guid.NewGuid(),
        TripId = transfer.Incoming.TripId,
        TruckId = transfer.ReplacementTruck.Id,
        Status = "active",
        Revision = 1,
      }
    );
    await f.Db.SaveChangesAsync();
    var command = await Command(f, transfer.Participant.ReceiveVisitId);
    command.Request.ChangeTruck = true;
    command.Request.TruckId = transfer.ReplacementTruck.Id;
    var result = await f.CorrectionHandler().Handle(command, default);
    Assert.Equal(400, result.StatusCode);
    Assert.Equal(1, transfer.Participant.Revision);
    Assert.Equal(0, await f.Db.DispatchWorkspaceRevisions.CountAsync());
  }

  private static async Task<CorrectDispatchStopCommand> Command(
    StopCompletionFixture f,
    Guid stopId
  )
  {
    var state = (
      await DispatchWorkspaceReader.ReadAsync(f.Db, f.Load.Id, true, default)
    )!;
    Assert.True(state.Response.Stops.Single(x => x.Id == stopId).CanCorrect);
    return new(
      f.Load.Id,
      stopId,
      new()
      {
        ExpectedRevision = state.Response.Revision,
        SourceFingerprint = state.Response.SourceFingerprint,
        IdempotencyKey = Guid.NewGuid(),
        Completion = "keep",
        ChangeAssignment = true,
        ChangeTruck = false,
        ChangeTrailer = false,
        ChangeDriver = false,
        ChangeCoDriver = false,
      }
    );
  }

  private sealed record Transfer(
    ExecutionLeg Outgoing,
    ExecutionLeg Incoming,
    SwitchParticipant Participant,
    Truck ReplacementTruck,
    Trailer ReplacementTrailer,
    Guid Actor,
    DateTime Released,
    DateTime Received
  );

  private static async Task<Transfer> Seed(StopCompletionFixture f)
  {
    var actor = await f.Db.Users.Select(x => x.Id).FirstAsync();
    var released = f.Clock.GetUtcNow().UtcDateTime.AddHours(-2);
    var received = released.AddHours(1);
    var trucks = Enumerable
      .Range(1, 3)
      .Select(i => new Truck
      {
        Id = Guid.NewGuid(),
        UnitNumber = $"Truck {i}",
        ExternalId = $"transfer-truck-{i}",
      })
      .ToArray();
    var oldTrailer = new Trailer
    {
      Id = Guid.NewGuid(),
      UnitNumber = "Old trailer",
      ExternalId = "transfer-old-trailer",
    };
    var newTrailer = new Trailer
    {
      Id = Guid.NewGuid(),
      UnitNumber = "New trailer",
      ExternalId = "transfer-new-trailer",
    };
    var trip = new Trip { Id = Guid.NewGuid() };
    var operation = new DispatchSwitchOperation
    {
      Id = Guid.NewGuid(),
      Status = "completed",
      Revision = 1,
      IdempotencyKey = Guid.NewGuid(),
      RecordedBy = actor,
      CompletedBy = actor,
      RecordedAt = released,
      CompletedAt = received,
    };
    var drop = new ExecutionTransferVisit
    {
      Id = Guid.NewGuid(),
      TripId = trip.Id,
      Operation = "Drop",
      SiteName = "Transfer yard",
      ActualAt = released,
      ConfirmedBy = actor,
      Revision = 1,
    };
    var hook = new ExecutionTransferVisit
    {
      Id = Guid.NewGuid(),
      TripId = trip.Id,
      Operation = "Hook",
      SiteName = "Transfer yard",
      ActualAt = received,
      ConfirmedBy = actor,
      Revision = 1,
    };
    var outgoing = new ExecutionLeg
    {
      Id = Guid.NewGuid(),
      TripId = trip.Id,
      TruckId = trucks[0].Id,
      TrailerId = oldTrailer.Id,
      Status = "completed",
      Revision = 1,
      EndSwitchId = operation.Id,
      Stops = ExecutionStopRows.Capture(
        [
          f.Load.Stops[0],
          ExecutionSnapshots.Boundary(drop, f.Load.Id, 2, "Loaded"),
        ]
      ),
    };
    var incoming = new ExecutionLeg
    {
      Id = Guid.NewGuid(),
      TripId = trip.Id,
      TruckId = trucks[1].Id,
      TrailerId = oldTrailer.Id,
      Status = "active",
      Revision = 1,
      StartSwitchId = operation.Id,
      Stops = ExecutionStopRows.Capture(
        new[]
        {
          ExecutionSnapshots.Boundary(hook, f.Load.Id, 3, "Loaded"),
        }.Concat(f.Load.Stops.Skip(1))
      ),
    };
    var participant = new SwitchParticipant
    {
      Id = Guid.NewGuid(),
      Switch = operation,
      DispatchId = f.Load.Id,
      OutgoingLegId = outgoing.Id,
      IncomingLegId = incoming.Id,
      ReleaseVisitId = drop.Id,
      ReceiveVisitId = hook.Id,
      TransferKind = "drop_hook",
      Revision = 1,
      ReleasedAt = released,
      ReceivedAt = received,
      ReleasedBy = actor,
      ReceivedBy = actor,
    };
    f.Db.AddRange(trucks);
    f.Db.AddRange(
      oldTrailer,
      newTrailer,
      trip,
      operation,
      outgoing,
      incoming,
      participant
    );
    f.Db.LoadExecutionLegs.AddRange(
      new LoadExecutionLeg
      {
        Id = Guid.NewGuid(),
        DispatchId = f.Load.Id,
        ExecutionLegId = outgoing.Id,
        Sequence = 1,
      },
      new LoadExecutionLeg
      {
        Id = Guid.NewGuid(),
        DispatchId = f.Load.Id,
        ExecutionLegId = incoming.Id,
        Sequence = 2,
      }
    );
    f.Db.TrailerCustodyIntervals.Add(
      new()
      {
        Id = Guid.NewGuid(),
        ParticipantId = participant.Id,
        TrailerId = oldTrailer.Id,
        ReleaseVisitId = drop.Id,
        ReceiveVisitId = hook.Id,
        ReleasedAt = released,
        ReceivedAt = received,
        ReleasedBy = actor,
        ReceivedBy = actor,
        Revision = 1,
      }
    );
    await f.Db.SaveChangesAsync();
    return new(
      outgoing,
      incoming,
      participant,
      trucks[2],
      newTrailer,
      actor,
      released,
      received
    );
  }
}
