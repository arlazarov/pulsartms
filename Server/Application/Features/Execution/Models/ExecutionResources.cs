namespace Application.Features.Execution.Models;

internal static class ExecutionResources
{
  public static bool Overlap(
    ExecutionAssignment left,
    ExecutionAssignment right
  ) =>
    left.TruckId == right.TruckId
    || left.TrailerId.HasValue && left.TrailerId == right.TrailerId
    || new[] { left.DriverId, left.CoDriverId }
      .Where(x => x.HasValue)
      .Intersect(new[] { right.DriverId, right.CoDriverId })
      .Any();

  public static async Task<bool> ActiveAsync(
    IAppDbContext db,
    IReadOnlyCollection<ExecutionAssignment> assignments,
    CancellationToken ct
  )
  {
    if (
      assignments.Any(x =>
        x.TruckId == Guid.Empty
        || x.DriverId.HasValue && x.DriverId == x.CoDriverId
      )
    )
      return false;
    var trucks = assignments.Select(x => x.TruckId).Distinct().ToArray();
    var drivers = assignments
      .SelectMany(x => new[] { x.DriverId, x.CoDriverId })
      .Where(x => x.HasValue)
      .Select(x => x!.Value)
      .Distinct()
      .ToArray();
    var trailers = assignments
      .Select(x => x.TrailerId)
      .Where(x => x.HasValue)
      .Select(x => x!.Value)
      .Distinct()
      .ToArray();
    return await db.Trucks.CountAsync(
        x => trucks.Contains(x.Id) && x.IsActive,
        ct
      ) == trucks.Length
      && await db.Drivers.CountAsync(
        x => drivers.Contains(x.Id) && x.IsActive,
        ct
      ) == drivers.Length
      && await db.Trailers.CountAsync(
        x => trailers.Contains(x.Id) && x.IsActive,
        ct
      ) == trailers.Length;
  }

  public static async Task<bool> ConflictsAsync(
    IAppDbContext db,
    ExecutionAssignment assignment,
    Guid? receivingParticipant,
    CancellationToken ct,
    Guid? excludingLeg = null
  )
  {
    var drivers = new[] { assignment.DriverId, assignment.CoDriverId }
      .Where(x => x.HasValue)
      .Select(x => x!.Value)
      .ToArray();
    if (
      await db.ExecutionLegs.AnyAsync(
        x =>
          x.Id != excludingLeg
          && x.Status == "active"
          && (
            x.TruckId == assignment.TruckId
            || assignment.TrailerId.HasValue
              && x.TrailerId == assignment.TrailerId
            || x.DriverId.HasValue
              && drivers.Contains(x.DriverId.Value)
              && (!x.Stops.Any() || x.Stops.Any(s => !s.HasDriverOverride))
            || x.CoDriverId.HasValue
              && drivers.Contains(x.CoDriverId.Value)
              && (!x.Stops.Any() || x.Stops.Any(s => !s.HasDriverOverride))
            || x.Stops.Any(s =>
              s.HasDriverOverride
              && (
                s.DriverId.HasValue && drivers.Contains(s.DriverId.Value)
                || s.CoDriverId.HasValue && drivers.Contains(s.CoDriverId.Value)
              )
            )
          ),
        ct
      )
    )
      return true;
    if (
      assignment.TrailerId.HasValue
      && await db.TrailerCustodyIntervals.AnyAsync(
        x =>
          x.TrailerId == assignment.TrailerId
          && x.ReceivedBy == null
          && x.ParticipantId != receivingParticipant,
        ct
      )
    )
      return true;
    return await (
      from participant in db.SwitchParticipants
      join outgoing in db.ExecutionLegs
        on participant.OutgoingLegId equals outgoing.Id
      join incoming in db.ExecutionLegs
        on participant.IncomingLegId equals incoming.Id
      where
        !participant.IsCancelled
        && participant.Id != receivingParticipant
        && participant.TransferKind == "resource_handoff"
        && participant.ReleasedBy != null
        && participant.ReceivedBy == null
        && (
          outgoing.TruckId == incoming.TruckId
            && incoming.TruckId == assignment.TruckId
          || incoming.TrailerId != null
            && outgoing.TrailerId == incoming.TrailerId
            && incoming.TrailerId == assignment.TrailerId
        )
      select participant.Id
    ).AnyAsync(ct);
  }
}
