using Application.Features.Execution.Models;
using Application.Features.Execution.Queries;
using Domain.Entities.Dispatch;
using Domain.Entities.Execution;

namespace Application.Features.Execution.Services;

internal sealed record ExecutionSourceReviewState(
  ExecutionLeg Leg,
  ExecutionSourceReview Review,
  IReadOnlyList<DispatchStop> Stops
);

internal static class ExecutionSourceReviewReader
{
  public static async Task<ExecutionSourceReviewState?> ReadAsync(
    IAppDbContext db,
    Guid dispatchId,
    Guid legId,
    DateTime now,
    CancellationToken ct
  )
  {
    var source = await db
      .Dispatches.AsNoTracking()
      .Include(x => x.Stops)
      .SingleOrDefaultAsync(x => x.Id == dispatchId, ct);
    var links = await db
      .LoadExecutionLegs.Include(x => x.ExecutionLeg)
      .ThenInclude(x => x.Loads)
      .Where(x => x.DispatchId == dispatchId)
      .OrderBy(x => x.Sequence)
      .ToListAsync(ct);
    var leg = links
      .SingleOrDefault(x => x.ExecutionLegId == legId)
      ?.ExecutionLeg;
    if (source is null || leg is null)
      return null;
    var problems = new List<string>();
    var sourceAssignment = await db
      .DispatchSourceLinks.Where(x => x.DispatchId == dispatchId)
      .Select(x => x.AssignmentSignature)
      .SingleOrDefaultAsync(ct);
    if (
      sourceAssignment is not null
      && sourceAssignment != leg.SourceAssignmentSignature
    )
      problems.Add(
        "Review the changed source resources in the assignment editor first."
      );
    if (leg.Status is not ("active" or "planned") || leg.Loads.Count != 1)
      problems.Add(
        "Only one-load active or planned assignments can be updated."
      );
    var visits = (
      await ExecutionTransfers.ReadAsync(
        db,
        links.Select(x => x.ExecutionLeg).ToArray(),
        ct
      )
    ).Values;
    var native = visits.Select(x => x.Id).ToHashSet();
    var mapped = visits
      .Where(x => x.SourceDispatchStopId.HasValue)
      .Select(x => x.SourceDispatchStopId!.Value)
      .ToHashSet();
    var snapshot = ExecutionStopRows.Read(leg);
    var savedOrder = links
      .SelectMany(x => ExecutionStopRows.Read(x.ExecutionLeg))
      .Where(x => !native.Contains(x.Id))
      .Select(x => x.Id);
    var sourceOrder = source
      .Stops.OrderBy(x => x.Sequence)
      .Where(x => !mapped.Contains(x.Id))
      .Select(x => x.Id);
    if (!savedOrder.SequenceEqual(sourceOrder))
      problems.Add("Added, removed or reordered visits need native editing.");
    var update = ExecutionSourceChanges.Preview(snapshot, source.Stops, native);
    problems.AddRange(update.Problems);
    var facts = ExecutionSourceFacts.Reconcile(
      leg,
      update.Stops,
      source,
      native,
      false,
      now
    );
    if (facts.ReviewReason is { } reason)
      problems.Add(reason);
    if (facts.Changed)
      problems.Add("Source actual events must synchronize before this update.");
    var changedIds = update.ChangedVisitIds.ToArray();
    if (
      await ExecutionAcceptance.HasProtectedPathAsync(db, leg, update.Stops, ct)
    )
      problems.Add("Affected visits already have recorded or manual mileage.");
    if (changedIds.Length == 0)
      problems.Add("There are no eligible future address or schedule changes.");
    return new(
      leg,
      new(
        dispatchId,
        leg.Id,
        leg.Revision,
        ExecutionSnapshots.Fingerprint(source),
        problems.Count == 0,
        problems.Distinct().ToArray(),
        changedIds
          .Select(id => new ExecutionSourceVisitChange(
            id,
            SwitchReads.Visit(snapshot.Single(x => x.Id == id)),
            SwitchReads.Visit(update.Stops.Single(x => x.Id == id))
          ))
          .ToArray()
      ),
      update.Stops
    );
  }
}
