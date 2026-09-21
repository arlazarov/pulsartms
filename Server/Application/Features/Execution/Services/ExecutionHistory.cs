using System.Collections.Immutable;
using System.Text.Json;
using Domain.Entities.Execution;
using Domain.Models.Execution;

namespace Application.Features.Execution.Services;

public static class ExecutionHistory
{
  public static async Task RecordAsync(
    IAppDbContext db,
    IEnumerable<ExecutionLeg> legs,
    string operation,
    Guid? actor,
    Guid? correlationId,
    DateTime now,
    CancellationToken ct
  )
  {
    if (db.Database.CurrentTransaction is null)
      throw new InvalidOperationException(
        "Execution history requires the owning mutation transaction."
      );
    var changed = legs.DistinctBy(x => x.Id).ToArray();
    if (changed.Length == 0)
      return;
    var actorName = actor.HasValue
      ? await db
        .Users.Where(x => x.Id == actor.Value)
        .Select(x => x.Name)
        .SingleOrDefaultAsync(ct)
      : null;
    var legIds = changed.Select(x => x.Id).ToArray();
    var participants = await db
      .SwitchParticipants.Where(x =>
        legIds.Contains(x.OutgoingLegId) || legIds.Contains(x.IncomingLegId)
      )
      .ToListAsync(ct);
    // Include participants created in the same uncommitted mutation.
    var transfers = ExecutionTransfers
      .Project(
        changed,
        participants
          .Concat(db.SwitchParticipants.Local)
          .DistinctBy(x => x.Id)
          .Where(x => db.Entry(x).State != EntityState.Deleted)
      )
      .Values;
    foreach (var leg in changed)
    {
      var ids = leg.Stops.Select(x => x.Id).ToHashSet();
      var facts = new ExecutionRevisionFacts(
        leg.TripId,
        leg.TruckId,
        leg.DriverId,
        leg.CoDriverId,
        leg.TrailerId,
        leg.Status,
        leg.StartSwitchId,
        leg.EndSwitchId,
        leg.StartedAt,
        leg.CompletedAt,
        leg.SourceSignature,
        leg.SourceReviewReason,
        leg.Stops.OrderBy(x => x.Position).ToImmutableArray(),
        leg.Loads.Where(x => db.Entry(x).State != EntityState.Deleted)
          .OrderBy(x => x.Sequence)
          .ThenBy(x => x.Id)
          .Select(x => new ExecutionRevisionLoad(
            x.Id,
            x.DispatchId,
            x.Sequence,
            x.StartVisitId,
            x.EndVisitId
          ))
          .ToImmutableArray(),
        transfers
          .Where(x => ids.Contains(x.Id))
          .OrderBy(x => x.Id)
          .ToImmutableArray()
      )
      {
        SourceAssignmentSignature = leg.SourceAssignmentSignature,
        ActorName = actorName,
      };
      db.ExecutionLegRevisions.Add(
        new()
        {
          ExecutionLegId = leg.Id,
          Revision = leg.Revision,
          TruckId = leg.TruckId,
          Operation = operation,
          RecordedBy = actor,
          CorrelationId = correlationId,
          RecordedAt = now,
          SnapshotJson = JsonSerializer.Serialize(facts),
        }
      );
    }
  }
}
