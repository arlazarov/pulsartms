using Application.Features.Execution.Models;
using Application.Features.Execution.Services;
using Domain.Entities.Dispatch;
using Domain.Entities.Execution;

namespace Server.Tests.Dispatch;

[Trait("Category", "Dispatch")]
[Trait("Kind", "Unit")]
public sealed class ExecutionTransferFactsTests
{
  [Theory]
  [InlineData(false)]
  [InlineData(true)]
  public void ConfirmationHasOneOwnerAndDoesNotRequireAnActualTime(
    bool confirmed
  )
  {
    var id = Guid.NewGuid();
    var actor = Guid.NewGuid();
    var stop = new DispatchStop
    {
      Id = id,
      DeliveredAt = DateTime.UtcNow,
      ManualCompletedAt = DateTime.UtcNow,
      CompletionOverride = true,
      ExecutionCompleted = true,
    };
    ExecutionSnapshots.ApplyActual(
      stop,
      new()
      {
        Id = id,
        ConfirmedBy = confirmed ? actor : null,
        ActualAt = null,
        Revision = 3,
      }
    );

    Assert.Equal(confirmed, stop.IsCompleted);
    Assert.Equal(confirmed, stop.ExecutionCompleted);
    Assert.Null(stop.DeliveredAt);
    Assert.Null(stop.ManualCompletedAt);
    Assert.Null(stop.CompletionOverride);
    Assert.Equal(confirmed ? actor : null, stop.ManualCompletedBy);
  }

  [Fact]
  public void BoundaryLocationAndIndependentActualsComeFromTheirOwners()
  {
    var release = Leg("Drop", 40);
    var receive = Leg("Hook", 41);
    var sourceId = Guid.NewGuid();
    receive.Stops[0].SourceDispatchStopId = sourceId;
    var actor = Guid.NewGuid();
    var at = new DateTime(2026, 9, 16, 8, 0, 0, DateTimeKind.Utc);
    var participant = new SwitchParticipant
    {
      Id = Guid.NewGuid(),
      OutgoingLegId = release.Id,
      IncomingLegId = receive.Id,
      ReleaseVisitId = release.Stops[0].Id,
      ReceiveVisitId = receive.Stops[0].Id,
      ReleasedBy = actor,
      ReleasedAt = at,
      PlannedReceiveAt = at.AddDays(1),
      Revision = 2,
    };

    var facts = ExecutionTransfers.Project([release, receive], [participant]);

    Assert.Equal(2, facts.Count);
    var drop = facts[participant.ReleaseVisitId];
    var hook = facts[participant.ReceiveVisitId];
    Assert.Equal(40m, drop.Latitude);
    Assert.Equal(41m, hook.Latitude);
    Assert.Equal("Hook", hook.Operation);
    Assert.Equal(sourceId, hook.SourceDispatchStopId);
    Assert.Equal(actor, drop.ConfirmedBy);
    Assert.Equal(at, drop.ActualAt);
    Assert.Null(hook.ConfirmedBy);
    Assert.Null(hook.ActualAt);
    Assert.Equal(at.AddDays(1), hook.PlannedAt);
    Assert.Single(ExecutionTransfers.Project([receive], [participant]));
  }

  private static ExecutionLeg Leg(string operation, decimal latitude) =>
    new()
    {
      Id = Guid.NewGuid(),
      TripId = Guid.NewGuid(),
      Stops =
      [
        new()
        {
          Id = Guid.NewGuid(),
          Job = operation,
          Name = "Accepted transfer site",
          Latitude = latitude,
          Longitude = -80,
        },
      ],
    };
}
