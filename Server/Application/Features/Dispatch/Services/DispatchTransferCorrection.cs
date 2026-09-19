using Application.Features.Dispatch.Models;
using Domain.Entities.Execution;

namespace Application.Features.Dispatch.Services;

public sealed record DispatchTransferCorrectionPlan(
  SwitchParticipant Participant,
  DispatchSwitchOperation Operation,
  List<TrailerCustodyInterval> Custody,
  Guid? TrailerId
);

public static class DispatchTransferCorrection
{
  public static async Task<(
    DispatchTransferCorrectionPlan? Plan,
    string? Error
  )> PrepareAsync(
    IAppDbContext db,
    DispatchWorkspaceState state,
    Guid selectedId,
    StopCorrectionRequest request,
    List<DispatchCorrectionTarget> targets,
    CancellationToken ct
  )
  {
    var transfer = state
      .Response.Stops.Single(x => x.Id == selectedId)
      .Transfer;
    if (
      transfer is null
      || request.Completion == "keep"
        && request.ChangeTruck == false
        && request.ChangeTrailer == false
        && (request.ChangeDriver == true || request.ChangeCoDriver == true)
    )
      return (null, null);
    if (
      request.Completion != "keep"
      || !request.ChangeAssignment
      || request.AssignmentScope != "current"
    )
      return (
        null,
        "Transfer confirmations stay unchanged. Edit the resources for this transfer."
      );
    var participant = await db
      .SwitchParticipants.Include(x => x.Switch)
      .SingleOrDefaultAsync(
        x =>
          x.SwitchId == transfer.SwitchId
          && x.DispatchId == state.Load.Id
          && (x.ReleaseVisitId == selectedId || x.ReceiveVisitId == selectedId),
        ct
      );
    if (
      participant is null
      || participant.IsCancelled
      || participant.Switch.Status == "cancelled"
    )
      return (null, "The transfer changed. Reload this load.");
    var ids = new[] { participant.OutgoingLegId, participant.IncomingLegId };
    var pair = state.Legs.Where(x => ids.Contains(x.Id)).ToList();
    if (
      pair.Count != 2
      || pair.Any(x => x.Loads.Count != 1 || x.Status == "cancelled")
    )
      return (
        null,
        "This transfer shares assignments with other loads and needs a coordinated correction."
      );
    var current = targets.Single(x => x.StopId == selectedId);
    var changesTrailer = current.Request.TrailerId != current.Leg!.TrailerId;
    var custody = new List<TrailerCustodyInterval>();
    if (changesTrailer)
    {
      if (participant.TransferKind != "drop_hook" || request.TrailerId is null)
        return (
          null,
          "A Drop/Hook must keep one assigned trailer on both sides."
        );
      if (
        await db.SwitchParticipants.AnyAsync(
          x =>
            !x.IsCancelled
            && x.Id != participant.Id
            && (ids.Contains(x.OutgoingLegId) || ids.Contains(x.IncomingLegId)),
          ct
        )
      )
        return (
          null,
          "This trailer continues through another transfer. Its connected transfers must be corrected together."
        );
      custody = await db
        .TrailerCustodyIntervals.Where(x => x.ParticipantId == participant.Id)
        .ToListAsync(ct);
      if (
        custody.Count > 1
        || participant.ReleasedBy.HasValue && custody.Count != 1
      )
        return (
          null,
          "The trailer custody history needs review before changing equipment."
        );
      if (
        await db.TrailerCustodyIntervals.AnyAsync(
          x =>
            x.TrailerId == request.TrailerId
            && x.ParticipantId != participant.Id
            && (
              x.ReceivedAt == null
              || participant.ReleasedAt == null
              || x.ReceivedAt > participant.ReleasedAt
            )
            && (
              participant.ReceivedAt == null
              || x.ReleasedAt == null
              || x.ReleasedAt < participant.ReceivedAt
            ),
          ct
        )
      )
        return (
          null,
          "That trailer has overlapping custody at another transfer."
        );
      var other = pair.Single(x => x.Id != current.Leg.Id);
      var otherStop = state.Response.Stops.First(x =>
        x.ExecutionLegId == other.Id
      );
      targets.Add(
        new(
          other,
          otherStop.Id,
          new StopCorrectionRequest
          {
            Completion = "keep",
            ChangeAssignment = true,
            ChangeTruck = false,
            ChangeTrailer = true,
            ChangeDriver = false,
            ChangeCoDriver = false,
            TruckId = other.TruckId,
            TrailerId = request.TrailerId,
            DriverId = other.DriverId,
            CoDriverId = other.CoDriverId,
          }
        )
      );
    }
    return (
      new(
        participant,
        participant.Switch,
        custody,
        changesTrailer ? request.TrailerId : null
      ),
      null
    );
  }

  public static void Apply(DispatchTransferCorrectionPlan plan)
  {
    if (plan.TrailerId is { } trailer)
      foreach (var interval in plan.Custody)
      {
        interval.TrailerId = trailer;
        interval.Revision++;
      }
    // Resource corrections do not confirm, reopen or retime either actual event.
    plan.Participant.Revision++;
    plan.Operation.Revision++;
  }
}
