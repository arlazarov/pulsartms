using Application.Diagnostics.Consistency;
using Domain.Rules;

namespace Application.Features.Execution.Audit;

// Source-cancelled loads, read the way ExecutionSourceReconciliation decides
// them. Runnable (planned or active) work of a cancelled source load is a
// violation: reconciliation moves it out in the same transaction that
// records the cancellation. Held work is the expected review, reported
// separately so fixing the first never hides the second.
internal static class CancelledSourceWork
{
  public static async Task<ConsistencyPage> ReadAsync(
    IAppDbContext db,
    ConsistencyPageRequest request,
    string[] statuses,
    CancellationToken ct
  )
  {
    Guid? after = request.After is null ? null : Guid.Parse(request.After);
    var rows = await db
      .LoadExecutionLegs.AsNoTracking()
      .Where(link =>
        statuses.Contains(link.ExecutionLeg.Status)
        && db.Dispatches.Any(load =>
          load.Id == link.DispatchId
          && SourceWords.Cancelled.Contains(load.Status.ToLower())
        )
        && db.DispatchSourceLinks.Any(source =>
          source.DispatchId == link.DispatchId
        )
        && (after == null || link.Id.CompareTo(after.Value) > 0)
      )
      .OrderBy(link => link.Id)
      .Select(link => new
      {
        link.Id,
        link.DispatchId,
        link.ExecutionLegId,
        link.ExecutionLeg.Status,
        link.ExecutionLeg.Revision,
        link.ExecutionLeg.TruckId,
        link.ExecutionLeg.RecordedAt,
        LoadNumber = db
          .Dispatches.Where(load => load.Id == link.DispatchId)
          .Select(load => load.LoadNumber)
          .FirstOrDefault(),
      })
      .Take(request.Limit + 1)
      .ToListAsync(ct);
    return new(
      [
        .. rows.Take(request.Limit)
          .Select(x => new ConsistencyObservation(
            x.Id.ToString(),
            $"leg:{x.Revision};status:{x.Status}",
            new Dictionary<string, string>
            {
              ["dispatchId"] = x.DispatchId.ToString(),
              ["loadNumber"] = x.LoadNumber.ToString(),
              ["executionLegId"] = x.ExecutionLegId.ToString(),
              ["legStatus"] = x.Status,
              ["legRevision"] = x.Revision.ToString(),
              ["truckId"] = x.TruckId.ToString(),
              ["legRecordedAt"] = x.RecordedAt.ToString("O"),
            }
          )),
      ],
      rows.Count > request.Limit
    );
  }
}

public sealed class CancelledSourceRunnableRule(IAppDbContext db)
  : IConsistencyRule
{
  public ConsistencyRuleInfo Info { get; } =
    new(
      "execution.cancelled-source-runnable",
      1,
      "Execution: ExecutionSourceReconciliation",
      ConsistencyCondition.Violation,
      ConsistencySeverity.Critical,
      "A load cancelled at its source has no planned or active execution.",
      "Run dispatch synchronization; its repair pass holds or cancels this "
        + "work. If it stays, synchronization is failing for this load."
    );

  public Task<ConsistencyPage> ReadAsync(
    ConsistencyPageRequest request,
    CancellationToken ct
  ) => CancelledSourceWork.ReadAsync(db, request, ["planned", "active"], ct);
}

public sealed class CancelledSourceHeldRule(IAppDbContext db) : IConsistencyRule
{
  public ConsistencyRuleInfo Info { get; } =
    new(
      "execution.cancelled-source-held",
      1,
      "Execution: CloseCancelledExecution",
      ConsistencyCondition.Review,
      ConsistencySeverity.Warning,
      "Started or locally changed work of a cancelled source load is held "
        + "until a dispatcher reviews and closes it.",
      "Review the load in Dispatch details and close the cancelled work."
    );

  public Task<ConsistencyPage> ReadAsync(
    ConsistencyPageRequest request,
    CancellationToken ct
  ) =>
    CancelledSourceWork.ReadAsync(db, request, [SourceCancellation.Held], ct);
}
