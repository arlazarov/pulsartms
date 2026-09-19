using Application.Features.Dispatch.Services;
using Application.Features.Execution.Commands;
using Application.Features.Execution.Models;
using Application.Features.Execution.Services;
using Domain.Entities.Execution;
using Domain.Entities.Fleet;
using Microsoft.EntityFrameworkCore;

namespace Server.Tests.Dispatch;

[Trait("Category", "Dispatch")]
[Trait("Kind", "Integration")]
public sealed class ExecutionHistoryTests
{
  [Theory]
  [InlineData(false)]
  [InlineData(true)]
  public async Task AcceptedHistoryRetainsItsActorAfterAccountChanges(
    bool delete
  )
  {
    await using var f = await StopCompletionFixture.CreateAsync();
    var name = f.Actor.Name;
    var actorId = f.Actor.Id;
    await Plan(f);
    if (delete)
      f.Db.Users.Remove(f.Actor);
    else
      f.Actor.Name = "Renamed operator";
    await f.Db.SaveChangesAsync();
    f.Db.ChangeTracker.Clear();
    var history = await f.Db.ExecutionLegRevisions.ToListAsync();
    Assert.Equal(2, history.Count);
    Assert.All(
      history,
      revision =>
      {
        Assert.Equal(actorId, revision.RecordedBy);
        Assert.Equal(name, ExecutionRevisionFacts.Read(revision).ActorName);
      }
    );
  }

  [Fact]
  public async Task CommercialEditDoesNotRewriteNativeOrderOrRevision()
  {
    await using var f = await StopCompletionFixture.CreateAsync();
    await Plan(f);
    var state = await DispatchWorkspaceReader.ReadAsync(
      f.Db,
      f.Load.Id,
      true,
      default
    );
    var before = await f.Db.ExecutionLegs.ToDictionaryAsync(
      x => x.Id,
      x => x.Revision
    );
    state!.Response.Metadata.BrokerContact = "Updated commercial contact";

    var result = await f.WorkspaceEditor()
      .Handle(
        new(
          f.Load.Id,
          new()
          {
            ExpectedRevision = state.Response.Revision,
            SourceFingerprint = state.Response.SourceFingerprint,
            IdempotencyKey = Guid.NewGuid(),
            Metadata = state.Response.Metadata,
            Stops = state.Response.Stops,
          }
        ),
        default
      );

    Assert.True(result.Success, string.Join(";", result.Errors ?? []));
    foreach (var leg in await f.Db.ExecutionLegs.ToListAsync())
      Assert.Equal(before[leg.Id], leg.Revision);
    Assert.Equal(2, await f.Db.ExecutionLegRevisions.CountAsync());
  }

  [Fact]
  public async Task AcceptedSourceCorrectionRetainsThePreviouslyAcceptedAddress()
  {
    await using var f = await StopCompletionFixture.CreateAsync();
    var plan = await Plan(f);
    var source = f.Load.Stops[3];
    var oldAddress = source.Address;
    source.Address = "20 Corrected Road";
    source.Latitude = 36;
    source.Longitude = -81;
    await f.Db.SaveChangesAsync();
    var preview = await f.SourceReview()
      .Handle(new(f.Load.Id, plan.Participant.IncomingLegId), default);
    Assert.True(preview.Success);
    Assert.True(
      preview.Response!.CanApply,
      string.Join(";", preview.Response.Problems)
    );
    var command = new AcceptExecutionSourceChangesCommand(
      f.Load.Id,
      new(
        plan.Participant.IncomingLegId,
        preview.Response.AssignmentRevision,
        preview.Response.SourceSignature,
        Guid.NewGuid()
      )
    );

    var result = await f.SourceAcceptance().Handle(command, default);

    Assert.True(result.Success, string.Join(";", result.Errors ?? []));
    Assert.True((await f.SourceAcceptance().Handle(command, default)).Success);
    Assert.Equal(3, await f.Db.ExecutionLegRevisions.CountAsync());
    var history = await f
      .Db.ExecutionLegRevisions.Where(x =>
        x.ExecutionLegId == plan.Participant.IncomingLegId
      )
      .OrderBy(x => x.Revision)
      .ToListAsync();
    Assert.Equal(
      oldAddress,
      ExecutionRevisionFacts
        .Read(history[0])
        .Stops.Single(x => x.Id == source.Id)
        .Address
    );
    Assert.Equal(
      source.Address,
      ExecutionRevisionFacts
        .Read(history[1])
        .Stops.Single(x => x.Id == source.Id)
        .Address
    );
    Assert.Equal("source-accepted", history[1].Operation);
    Assert.Equal(command.Request.IdempotencyKey, history[1].CorrelationId);
  }

  [Fact]
  public async Task HeaderAssignmentChangeInvalidatesAnOpenedSourceReview()
  {
    await using var f = await StopCompletionFixture.CreateAsync();
    var plan = await Plan(f);
    f.Load.Stops[3].Address = "20 Corrected Road";
    f.Load.Stops[3].Latitude = 36;
    f.Load.Stops[3].Longitude = -81;
    await f.Db.SaveChangesAsync();
    var preview = await f.SourceReview()
      .Handle(new(f.Load.Id, plan.Participant.IncomingLegId), default);
    Assert.True(
      preview.Response!.CanApply,
      string.Join(";", preview.Response.Problems)
    );
    f.Load.TruckNumber = "Changed while review was open";
    await f.Db.SaveChangesAsync();

    var result = await f.SourceAcceptance()
      .Handle(
        new(
          f.Load.Id,
          new(
            plan.Participant.IncomingLegId,
            preview.Response.AssignmentRevision,
            preview.Response.SourceSignature,
            Guid.NewGuid()
          )
        ),
        default
      );

    Assert.False(result.Success);
    Assert.Equal(2, await f.Db.ExecutionLegRevisions.CountAsync());
    Assert.Equal(0, await f.Db.ExecutionSourceReceipts.CountAsync());
    Assert.Null(plan.Participant.ReleasedBy);
    Assert.Null(plan.Participant.ReceivedBy);
  }

  [Fact]
  public async Task PlanningCancellationAndRetriesRetainBothAcceptedStates()
  {
    await using var f = await StopCompletionFixture.CreateAsync();
    var plan = await Plan(f);
    Assert.True(
      (await f.SwitchPlanner().Handle(plan.Command, default)).Success
    );
    Assert.Equal(2, await f.Db.ExecutionLegRevisions.CountAsync());
    var outgoing = await f.Db.ExecutionLegs.SingleAsync(x =>
      x.Id == plan.Participant.OutgoingLegId
    );
    var accepted = await f.Db.ExecutionLegRevisions.SingleAsync(x =>
      x.ExecutionLegId == outgoing.Id
    );
    var acceptedJson = accepted.SnapshotJson;
    var planned = ExecutionRevisionFacts.Read(accepted);
    Assert.Equal(plan.Command.Request.IdempotencyKey, accepted.CorrelationId);
    Assert.Equal(f.Actor.Id, accepted.RecordedBy);
    Assert.Equal(plan.Participant.ReleaseVisitId, planned.Stops[^1].Id);
    Assert.Null(Assert.Single(planned.Transfers).ConfirmedBy);
    var cancel = new CancelSwitchCommand(
      plan.Participant.SwitchId,
      new(Guid.NewGuid(), 1)
    );

    var result = await f.SwitchCancellation().Handle(cancel, default);

    Assert.True(result.Success, string.Join(";", result.Errors ?? []));
    Assert.True((await f.SwitchCancellation().Handle(cancel, default)).Success);
    Assert.Equal(4, await f.Db.ExecutionLegRevisions.CountAsync());
    Assert.Equal(acceptedJson, accepted.SnapshotJson);
    var cancelled = await f.Db.ExecutionLegRevisions.SingleAsync(x =>
      x.ExecutionLegId == plan.Participant.IncomingLegId && x.Revision == 2
    );
    var facts = ExecutionRevisionFacts.Read(cancelled);
    Assert.Equal("cancelled", facts.Status);
    Assert.Empty(facts.Loads);
    var restored = await f.Db.ExecutionLegRevisions.SingleAsync(x =>
      x.ExecutionLegId == outgoing.Id && x.Revision == accepted.Revision + 1
    );
    Assert.Equal(
      f.Load.Stops.Select(x => x.Id),
      ExecutionRevisionFacts.Read(restored).Stops.Select(x => x.Id)
    );
  }

  [Fact]
  public async Task ConfirmedTransfersKeepUnknownTimesAndPriorUnconfirmedFacts()
  {
    await using var f = await StopCompletionFixture.CreateAsync();
    var plan = await Plan(f);
    var participant = plan.Participant;
    var release = new ReleaseSwitchParticipantCommand(
      new(Guid.NewGuid(), participant.SwitchId, participant.Id, 1, 1, 1, null)
    );
    var released = await f.SwitchRelease().Handle(release, default);
    Assert.True(released.Success, string.Join(";", released.Errors ?? []));
    Assert.True((await f.SwitchRelease().Handle(release, default)).Success);
    var receive = new ReceiveSwitchParticipantCommand(
      new(Guid.NewGuid(), participant.SwitchId, participant.Id, 2, 2, 1, null)
    );
    var received = await f.SwitchReceipt().Handle(receive, default);
    Assert.True(received.Success, string.Join(";", received.Errors ?? []));
    Assert.True((await f.SwitchReceipt().Handle(receive, default)).Success);

    var history = await f.Db.ExecutionLegRevisions.ToListAsync();
    Assert.Equal(4, history.Count);
    foreach (var row in history)
    {
      var facts = ExecutionRevisionFacts.Read(row);
      var actual = Assert.Single(facts.Transfers);
      Assert.Null(actual.ActualAt);
      Assert.Null(facts.StartedAt);
      Assert.Null(facts.CompletedAt);
      Assert.Equal(
        row.Operation == "transfer-planned" ? null : f.Actor.Id,
        actual.ConfirmedBy
      );
    }
  }

  [Fact]
  public async Task RolledBackMutationLeavesNoHistoryOrAcceptedChanges()
  {
    await using var f = await StopCompletionFixture.CreateAsync();
    var plan = await Plan(f);
    var leg = await f.Db.ExecutionLegs.SingleAsync(x =>
      x.Id == plan.Participant.IncomingLegId
    );
    var original = leg.Stops[0].Notes;
    await using (var transaction = await f.Db.Database.BeginTransactionAsync())
    {
      leg.Stops[0].Notes = "Must not survive rollback";
      leg.Revision++;
      await ExecutionHistory.RecordAsync(
        f.Db,
        [leg],
        "workspace-updated",
        f.Actor.Id,
        Guid.NewGuid(),
        f.Clock.GetUtcNow().UtcDateTime,
        default
      );
      await f.Db.SaveChangesAsync();
      await transaction.RollbackAsync();
    }
    f.Db.ChangeTracker.Clear();
    var restored = await f.Db.ExecutionLegs.SingleAsync(x => x.Id == leg.Id);
    Assert.Equal(1, restored.Revision);
    Assert.Equal(original, restored.Stops[0].Notes);
    Assert.Equal(2, await f.Db.ExecutionLegRevisions.CountAsync());
  }

  [Theory]
  [InlineData(false)]
  [InlineData(true)]
  public async Task StoredHistoryRejectsRewritesAndDeletion(bool delete)
  {
    await using var f = await StopCompletionFixture.CreateAsync();
    await Plan(f);
    var row = await f.Db.ExecutionLegRevisions.FirstAsync();
    if (delete)
      f.Db.ExecutionLegRevisions.Remove(row);
    else
      f.Db.Entry(row).Property(x => x.SnapshotJson).CurrentValue = "{}";
    await Assert.ThrowsAsync<InvalidOperationException>(
      async () => await f.Db.SaveChangesAsync()
    );
    f.Db.ChangeTracker.Clear();
    Assert.Equal(2, await f.Db.ExecutionLegRevisions.CountAsync());
    Assert.NotEmpty(
      ExecutionRevisionFacts
        .Read(await f.Db.ExecutionLegRevisions.FirstAsync())
        .Stops
    );
  }

  [Fact]
  public async Task HistoryCannotBeWrittenOutsideTheMutationTransaction()
  {
    await using var f = await StopCompletionFixture.CreateAsync();
    await Assert.ThrowsAsync<InvalidOperationException>(
      () =>
        ExecutionHistory.RecordAsync(
          f.Db,
          [],
          "source-synchronized",
          null,
          null,
          f.Clock.GetUtcNow().UtcDateTime,
          default
        )
    );
  }

  private static async Task<(
    PlanSwitchCommand Command,
    SwitchParticipant Participant
  )> Plan(StopCompletionFixture f)
  {
    var outgoing = new Truck
    {
      Id = Guid.NewGuid(),
      ExternalId = "history-out",
      UnitNumber = "history-out",
      IsActive = true,
    };
    var incoming = new Truck
    {
      Id = Guid.NewGuid(),
      ExternalId = "history-in",
      UnitNumber = "history-in",
      IsActive = true,
    };
    f.Db.Trucks.AddRange(outgoing, incoming);
    f.Load.Status = "in_transit";
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
            new(outgoing.Id, null, null),
            new(incoming.Id, null, null)
          )
          {
            SplitAfterVisitId = f.Load.Stops[1].Id,
          },
        ]
      )
      {
        Latitude = 35,
        Longitude = -80,
      }
    );
    var result = await f.SwitchPlanner().Handle(command, default);
    Assert.True(result.Success, string.Join(";", result.Errors ?? []));
    return (command, await f.Db.SwitchParticipants.SingleAsync());
  }
}
