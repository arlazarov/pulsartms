using Application.Features.Dispatch.Services;
using Domain.Entities.Execution;

namespace Server.Tests.Dispatch;

[Trait("Category", "Dispatch")]
[Trait("Kind", "Unit")]
public sealed class DispatchWorkspaceTransferTests
{
  [Fact]
  public void DropAndHookHaveIndependentConfirmationWithoutInventedTimes()
  {
    var outgoing = new ExecutionLeg
    {
      Id = Guid.NewGuid(),
      TruckId = Guid.NewGuid(),
      TrailerId = Guid.NewGuid(),
      DriverId = Guid.NewGuid(),
    };
    var incoming = new ExecutionLeg
    {
      Id = Guid.NewGuid(),
      TruckId = Guid.NewGuid(),
      TrailerId = outgoing.TrailerId,
      DriverId = Guid.NewGuid(),
    };
    var participant = new SwitchParticipant
    {
      SwitchId = Guid.NewGuid(),
      OutgoingLegId = outgoing.Id,
      IncomingLegId = incoming.Id,
      ReleaseVisitId = Guid.NewGuid(),
      ReceiveVisitId = Guid.NewGuid(),
      TransferKind = "drop_hook",
      ReleasedBy = Guid.NewGuid(),
      PlannedReceiveAt = DateTime.UtcNow.AddDays(1),
    };
    Dictionary<Guid, string> trucks = new()
    {
      [outgoing.TruckId] = "11005",
      [incoming.TruckId] = "54777",
    };
    Dictionary<Guid, string> trailers = new()
    {
      [outgoing.TrailerId.Value] = "9P1175",
    };
    var drop = DispatchWorkspaceTransfers.Project(
      participant,
      participant.ReleaseVisitId,
      [outgoing, incoming],
      trucks,
      trailers,
      new Dictionary<Guid, string>()
    );
    var hook = DispatchWorkspaceTransfers.Project(
      participant,
      participant.ReceiveVisitId,
      [outgoing, incoming],
      trucks,
      trailers,
      new Dictionary<Guid, string>()
    );
    Assert.Equal("drop", drop!.Action);
    Assert.Equal("confirmed", drop.Status);
    Assert.Null(drop.ActualAt);
    Assert.Equal("11005", drop.OutgoingTruckNumber);
    Assert.Equal("54777", drop.IncomingTruckNumber);
    Assert.Equal("9P1175", drop.OutgoingTrailerNumber);
    Assert.Equal("9P1175", drop.IncomingTrailerNumber);
    Assert.Equal("hook", hook!.Action);
    Assert.Equal("planned", hook.Status);
    Assert.Null(hook.ActualAt);
    Assert.Null(
      DispatchWorkspaceTransfers.Project(
        participant,
        Guid.NewGuid(),
        [outgoing, incoming],
        trucks,
        trailers,
        new Dictionary<Guid, string>()
      )
    );
  }

  [Fact]
  public void CancelledTransferIsNotAnActiveStopOperation()
  {
    var participant = new SwitchParticipant
    {
      ReleaseVisitId = Guid.NewGuid(),
      IsCancelled = true,
    };
    Assert.Null(
      DispatchWorkspaceTransfers.Project(
        participant,
        participant.ReleaseVisitId,
        [],
        new Dictionary<Guid, string>(),
        new Dictionary<Guid, string>(),
        new Dictionary<Guid, string>()
      )
    );
  }

  [Fact]
  public void ResourceHandoffAndTimestampAloneDoNotClaimTrailerHook()
  {
    var outgoing = new ExecutionLeg { Id = Guid.NewGuid() };
    var incoming = new ExecutionLeg { Id = Guid.NewGuid() };
    var participant = new SwitchParticipant
    {
      OutgoingLegId = outgoing.Id,
      IncomingLegId = incoming.Id,
      ReceiveVisitId = Guid.NewGuid(),
      TransferKind = "resource_handoff",
      ReceivedAt = DateTime.UtcNow,
    };
    var result = DispatchWorkspaceTransfers.Project(
      participant,
      participant.ReceiveVisitId,
      [outgoing, incoming],
      new Dictionary<Guid, string>(),
      new Dictionary<Guid, string>(),
      new Dictionary<Guid, string>()
    );
    Assert.Equal("receive", result!.Action);
    Assert.Equal("resource_handoff", result.Kind);
    Assert.Equal("planned", result.Status);
    Assert.Null(result.ActualAt);
    Assert.Equal("", result.IncomingTrailerNumber);
  }
}
