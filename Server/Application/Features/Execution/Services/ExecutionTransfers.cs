using Domain.Entities.Execution;
using Domain.Models.Execution;

namespace Application.Features.Execution.Services;

public static class ExecutionTransfers
{
  public static async Task<Dictionary<Guid, ExecutionTransferVisit>> ReadAsync(
    IAppDbContext db,
    IReadOnlyCollection<ExecutionLeg> legs,
    CancellationToken ct
  )
  {
    if (legs.Count == 0)
      return [];
    var ids = legs.Select(x => x.Id).ToArray();
    var participants = await db
      .SwitchParticipants.AsNoTracking()
      .Where(x =>
        !x.IsCancelled
        && (ids.Contains(x.OutgoingLegId) || ids.Contains(x.IncomingLegId))
      )
      .ToListAsync(ct);
    return Project(legs, participants);
  }

  public static Dictionary<Guid, ExecutionTransferVisit> Project(
    IEnumerable<ExecutionLeg> legs,
    IEnumerable<SwitchParticipant> participants
  )
  {
    var byLeg = legs.ToDictionary(x => x.Id);
    var result = new Dictionary<Guid, ExecutionTransferVisit>();
    foreach (var participant in participants)
    {
      Add(participant, false);
      Add(participant, true);
    }
    return result;

    void Add(SwitchParticipant participant, bool receive)
    {
      var legId = receive
        ? participant.IncomingLegId
        : participant.OutgoingLegId;
      if (!byLeg.TryGetValue(legId, out var leg))
        return;
      var id = receive
        ? participant.ReceiveVisitId
        : participant.ReleaseVisitId;
      var stop = leg.Stops.SingleOrDefault(x => x.Id == id);
      if (stop is null)
        return;
      result.Add(
        id,
        new()
        {
          Id = id,
          TripId = leg.TripId,
          SourceDispatchStopId = stop.SourceDispatchStopId,
          Operation = stop.Job,
          SiteName = stop.Name,
          Latitude = stop.Latitude,
          Longitude = stop.Longitude,
          PlannedAt = receive
            ? participant.PlannedReceiveAt
            : participant.PlannedReleaseAt,
          ActualAt = receive ? participant.ReceivedAt : participant.ReleasedAt,
          ConfirmedBy = receive
            ? participant.ReceivedBy
            : participant.ReleasedBy,
          Revision = participant.Revision,
        }
      );
    }
  }
}
