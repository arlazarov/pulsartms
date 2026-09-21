using Domain.Entities.Execution;

namespace Application.Features.Execution.Models;

internal static class CompletedSwitchBootstrap
{
  public static void Apply(
    IAppDbContext db,
    DispatchSwitchOperation operation,
    SwitchParticipant participant,
    ExecutionLeg outgoing,
    ExecutionLeg incoming,
    Guid actor
  )
  {
    // Confirmation is not an exact event time or odometer boundary.
    participant.ReleasedBy = actor;
    participant.ReceivedBy = actor;
    outgoing.Status = "completed";
    incoming.Status = "active";
    incoming.Trip.Status = "active";
    operation.Status = "completed";
    operation.CompletedBy = actor;
    if (participant.TransferKind == "drop_hook")
      db.TrailerCustodyIntervals.Add(
        new()
        {
          Id = Guid.NewGuid(),
          TrailerId = incoming.TrailerId!.Value,
          ParticipantId = participant.Id,
          ReleaseVisitId = participant.ReleaseVisitId,
          ReceiveVisitId = participant.ReceiveVisitId,
          ReleasedBy = actor,
          ReceivedBy = actor,
          Revision = 1,
        }
      );
  }
}
