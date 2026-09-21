using System.Collections.Immutable;
using Domain.Models.Execution;

namespace Application.Features.Execution.Services;

public static class WorkSequenceReader
{
  public static async Task<WorkSequenceEvidence> ReadAsync(
    IAppDbContext db,
    IReadOnlyList<WorkLoadReference> work,
    CancellationToken ct
  )
  {
    ct.ThrowIfCancellationRequested();
    var native = work.Where(x => x.ExecutionLegId.HasValue).ToArray();
    if (native.Length == 0)
      return WorkSequenceEvidence.Empty;
    var dispatchIds = native.Select(x => x.Id).Distinct().ToArray();
    var legIds = native
      .Select(x => x.ExecutionLegId!.Value)
      .Distinct()
      .ToArray();
    var legs = await db
      .LoadExecutionLegs.AsNoTracking()
      .Where(x => dispatchIds.Contains(x.DispatchId))
      .OrderBy(x => x.DispatchId)
      .ThenBy(x => x.Sequence)
      .Select(x => new WorkLegPosition(
        x.DispatchId,
        x.ExecutionLegId,
        x.Sequence,
        x.ExecutionLeg.TruckId,
        x.ExecutionLeg.Status,
        x.ExecutionLeg.Revision,
        x.StartVisitId,
        x.EndVisitId
      ))
      .ToArrayAsync(ct);
    var transfers = await db
      .SwitchParticipants.AsNoTracking()
      .Where(x =>
        !x.IsCancelled
        && (
          legIds.Contains(x.OutgoingLegId) || legIds.Contains(x.IncomingLegId)
        )
      )
      .OrderBy(x => x.Id)
      .Select(x => new WorkTransferDependency(
        x.Id,
        x.DispatchId,
        x.OutgoingLegId,
        x.IncomingLegId,
        x.ReleasedBy.HasValue,
        x.ReceivedBy.HasValue,
        x.Revision,
        x.SwitchId,
        x.ReleaseVisitId,
        x.ReceiveVisitId,
        x.PlannedReleaseAt,
        x.PlannedReceiveAt,
        x.ReleasedAt,
        x.ReceivedAt
      ))
      .ToArrayAsync(ct);
    return new(legs.ToImmutableArray(), transfers.ToImmutableArray());
  }
}
