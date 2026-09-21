using System.Text.Json;
using Application.Features.Execution.Models;
using Application.Features.Execution.Services;
using Domain.Entities.Execution;
using Domain.Models.Execution;

namespace Application.Features.Execution.Commands;

// The two halves of a transfer as the drivers perform them: the outgoing
// driver releasing the load, and the incoming driver receiving it. Each
// returns the reason it cannot happen yet, or null once it has.
internal sealed partial class SwitchParticipantMutation
{
  private async Task<string?> ReleaseAsync(
    SwitchParticipant participant,
    ExecutionLeg leg,
    DateTime? at,
    Guid actor,
    CancellationToken ct
  )
  {
    if (
      leg.Status != "active"
      || participant.ReleasedBy.HasValue
      || participant.ReceivedBy.HasValue
      || leg.StartedAt > at
    )
      return "Only an active, unreleased assignment can be released.";
    var stops = ExecutionStopRows.Read(leg);
    if (
      stops.Any(x =>
        (
          x.DepartedAt
          ?? x.DeliveredAt
          ?? x.PickedUpAt
          ?? x.ManualCompletedAt
          ?? x.ArrivedAt
        ) > at
      )
    )
      return "The release cannot precede recorded work on this assignment.";
    var sourceIds = stops.Select(x => x.Id).ToArray();
    if (
      await db.DispatchStops.AnyAsync(
        x =>
          sourceIds.Contains(x.Id)
          && (
            x.DepartedAt
            ?? x.DeliveredAt
            ?? x.PickedUpAt
            ?? x.ManualCompletedAt
            ?? x.ArrivedAt
          ) > at,
        ct
      )
    )
      return "Source actuals changed. The release time needs reconciliation.";
    if (
      await db.Movements.AnyAsync(
        x =>
          x.ExecutionLegId == leg.Id
          && (
            x.StartedAt >= at
            || x.EndedAt > at
            || x.StartedAt != null && x.EndedAt == null
          ),
        ct
      )
    )
      return "Close or correct recorded movement "
        + "before releasing this assignment.";
    if (participant.TransferKind == "drop_hook")
    {
      if (
        leg.TrailerId is not { } trailer
        || await db.TrailerCustodyIntervals.AnyAsync(
          x => x.TrailerId == trailer && x.ReceivedBy == null,
          ct
        )
      )
        return "The trailer already has open custody or is not assigned.";
      db.TrailerCustodyIntervals.Add(
        new()
        {
          Id = Guid.NewGuid(),
          TrailerId = trailer,
          ParticipantId = participant.Id,
          ReleaseVisitId = participant.ReleaseVisitId,
          ReceiveVisitId = participant.ReceiveVisitId,
          ReleasedAt = at,
          ReleasedBy = actor,
          Revision = 1,
        }
      );
    }
    participant.ReleasedAt = at;
    participant.ReleasedBy = actor;
    leg.Status = "completed";
    leg.CompletedAt = at;
    return null;
  }

  private async Task<string?> ReceiveAsync(
    SwitchParticipant participant,
    ExecutionLeg leg,
    DateTime? at,
    Guid actor,
    CancellationToken ct
  )
  {
    if (
      leg.Status != "planned"
      || !participant.ReleasedBy.HasValue
      || participant.ReceivedBy.HasValue
      || participant.ReleasedAt > at
    )
      return "Receipt requires its own confirmed release; "
        + "a known receipt time cannot precede release.";
    var assignment = ExecutionCommandSupport.Assignment(leg);
    if (
      !await ExecutionResources.ActiveAsync(db, [assignment], ct)
      || await ExecutionResources.ConflictsAsync(
        db,
        assignment,
        participant.Id,
        ct
      )
    )
      return "Incoming resources are inactive, attached or assigned elsewhere.";
    var drivers = new[] { assignment.DriverId, assignment.CoDriverId }
      .Where(x => x.HasValue)
      .Select(x => x!.Value)
      .ToArray();
    if (
      await db.ExecutionLegs.AnyAsync(
        x =>
          x.CompletedAt > at
          && (
            x.TruckId == leg.TruckId
            || leg.TrailerId != null && x.TrailerId == leg.TrailerId
            || x.DriverId != null && drivers.Contains(x.DriverId.Value)
            || x.CoDriverId != null && drivers.Contains(x.CoDriverId.Value)
          ),
        ct
      )
    )
      return "The receiving time overlaps already recorded resource work.";
    if (participant.TransferKind == "drop_hook")
    {
      var custody = await db.TrailerCustodyIntervals.SingleOrDefaultAsync(
        x => x.ParticipantId == participant.Id && x.ReceivedBy == null,
        ct
      );
      if (
        custody is null
        || custody.TrailerId != leg.TrailerId
        || custody.ReleasedAt > at
      )
        return "No matching released trailer is available for this hook.";
      custody.ReceivedAt = at;
      custody.ReceivedBy = actor;
      custody.Revision++;
    }
    participant.ReceivedAt = at;
    participant.ReceivedBy = actor;
    leg.Status = "active";
    leg.StartedAt = at;
    leg.Trip.Status = "active";
    leg.Trip.Revision++;
    return null;
  }
}
