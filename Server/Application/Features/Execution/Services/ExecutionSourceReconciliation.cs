using Application.Features.Dispatch.Models;
using Application.Features.Execution.Models;
using Domain.Entities.Dispatch;
using Domain.Entities.Execution;
using DispatchEntity = Domain.Entities.Dispatch.Dispatch;

namespace Application.Features.Execution.Services;

public static class ExecutionSourceReconciliation
{
  public static async Task<IReadOnlyList<ExecutionLeg>> ApplyAsync(
    IAppDbContext db,
    IReadOnlyCollection<DispatchEntity> sources,
    DateTime now,
    CancellationToken ct
  )
  {
    if (db.Database.CurrentTransaction is null)
      throw new InvalidOperationException(
        "Execution reconciliation requires a transaction."
      );
    if (sources.Count == 0)
      return [];
    var sourceById = sources.ToDictionary(x => x.Id);
    var sourceIds = sources.Select(x => x.Id).ToArray();
    var observations = await db
      .DispatchSourceLinks.Where(x => sourceIds.Contains(x.DispatchId))
      .ToDictionaryAsync(x => x.DispatchId, ct);
    var links = await db
      .LoadExecutionLegs.Include(x => x.ExecutionLeg)
      .ThenInclude(x => x.Loads)
      .Where(x => sourceIds.Contains(x.DispatchId))
      .ToListAsync(ct);
    var legs = links
      .Select(x => x.ExecutionLeg)
      .DistinctBy(x => x.Id)
      .OrderBy(x => x.Id)
      .ToArray();
    if (!legs.Any(x => x.Status is "active" or "planned"))
      return [];
    var linksByLeg = links.ToLookup(x => x.ExecutionLegId);
    var linksBySource = links
      .OrderBy(x => x.Sequence)
      .ToLookup(x => x.DispatchId);
    var nativeVisits = (
      await ExecutionTransfers.ReadAsync(db, legs, ct)
    ).Values.ToArray();
    var visitIds = nativeVisits.Select(x => x.Id).ToHashSet();
    var confirmedTransfers = nativeVisits
      .Where(x => x.ConfirmedBy.HasValue)
      .Select(x => x.Id)
      .ToHashSet();
    var snapshots = new Dictionary<Guid, List<DispatchStop>>();
    var fingerprints = new Dictionary<Guid, string>();
    var topology = new Dictionary<Guid, ExecutionSourceTopology>();
    var changes = new List<ExecutionChange>();
    var changedFacts = new HashSet<Guid>();
    var reviews = new Dictionary<Guid, ExecutionReviewObservation>();
    foreach (var leg in legs.Where(x => x.Status is "active" or "planned"))
    {
      var legLinks = linksByLeg[leg.Id].ToArray();
      if (legLinks.Length != 1 || leg.Loads.Count != 1)
      {
        Observe(leg, "Shared-load execution needs source reconciliation.");
        continue;
      }
      var source = sourceById[legLinks[0].DispatchId];
      var sourceAssignment = observations.GetValueOrDefault(source.Id);
      DispatchResourceProposal? replacement = null;
      if (
        sourceAssignment is not null
        && sourceAssignment.AssignmentSignature != leg.SourceAssignmentSignature
      )
      {
        replacement = ExecutionSourceAssignment.Resolve(
          sourceAssignment,
          linksBySource[source.Id].Count() == 1
            && !nativeVisits.Any(x => ReadSnapshot(leg).Any(s => s.Id == x.Id))
        );
        if (
          replacement is null
          || !await ExecutionResources.ActiveAsync(
            db,
            [
              new(
                replacement.TruckId!.Value,
                replacement.DriverId,
                replacement.TrailerId,
                replacement.CoDriverId
              ),
            ],
            ct
          )
        )
        {
          Observe(
            leg,
            "Source resources are unresolved or have ambiguous execution boundaries."
          );
          continue;
        }
      }

      if (!fingerprints.TryGetValue(source.Id, out var observed))
      {
        observed = ExecutionSnapshots.Fingerprint(source);
        fingerprints.Add(source.Id, observed);
      }
      if (!topology.TryGetValue(source.Id, out var sourceTopology))
      {
        var sourceLegs = linksBySource[source.Id]
          .Select(x => x.ExecutionLeg)
          .ToArray();
        foreach (var sourceLeg in sourceLegs)
          ReadSnapshot(sourceLeg);
        sourceTopology = ExecutionSourceTopology.Resolve(
          source,
          sourceLegs,
          snapshots,
          nativeVisits
        );
        topology.Add(source.Id, sourceTopology);
      }
      var snapshot = sourceTopology.Stops[leg.Id];
      var added = sourceTopology.ChangedLegIds.Contains(leg.Id);
      var result = ExecutionSourceFacts.Reconcile(
        leg,
        snapshot,
        source,
        visitIds,
        sourceTopology.NeedsReview,
        now,
        confirmedTransfers
      );
      var review = result.ReviewReason;
      var complete = result.CompletedAt;
      var status = result.Status;
      var accepted = result.Stops;
      var factsChanged = result.Changed;
      if (
        await ExecutionAcceptance.HasProtectedPathAsync(db, leg, accepted, ct)
      )
      {
        review =
          "Source visits conflict with recorded mileage. "
          + "Review the execution path before accepting these visits.";
        accepted = ReadSnapshot(leg);
        factsChanged = false;
        added = false;
        complete = null;
        status = leg.Status;
      }
      if (
        complete.HasValue
        && await db.Movements.AnyAsync(
          x =>
            x.ExecutionLegId == leg.Id
            && (
              x.StartedAt != null && x.EndedAt == null || x.EndedAt > complete
            ),
          ct
        )
      )
      {
        review =
          "Delivery conflicts with recorded movement. "
          + "Review actual execution before closing it.";
        complete = null;
        status = leg.Status;
      }
      var previousTruck = leg.TruckId;
      if (replacement is not null)
      {
        ExecutionSourceAssignment.Apply(
          leg,
          accepted,
          replacement,
          sourceAssignment!.AssignmentSignature
        );
        factsChanged = true;
      }
      if (factsChanged || added)
        changedFacts.Add(leg.Id);
      if (factsChanged || added || complete.HasValue || status != leg.Status)
      {
        changes.Add(
          new(leg, accepted)
          {
            UpdateBoundaries = added,
            SupersedePlannedMileage = replacement is not null,
            PreviousTruckId =
              previousTruck != leg.TruckId ? previousTruck : null,
            SourceSignature =
              added && review is null ? observed : leg.SourceSignature,
            ReviewReason = review,
            CompletedAt = complete,
            Status = status,
          }
        );
      }
      else
        Observe(leg, review);
      leg.SourceObservedSignature = observed;
    }
    var activations = changes
      .Where(x => x.Leg.Status == "planned" && x.Status == "active")
      .ToArray();
    foreach (var activation in activations)
    {
      var assignment = Assignment(activation.Leg);
      if (
        activations.Any(x =>
          x != activation
          && ExecutionResources.Overlap(assignment, Assignment(x.Leg))
        )
        || !await ExecutionResources.ActiveAsync(db, [assignment], ct)
        || await ExecutionResources.ConflictsAsync(
          db,
          assignment,
          null,
          ct,
          activation.Leg.Id
        )
      )
      {
        changes.Remove(activation);
        const string reason =
          "Source work conflicts with an unavailable resource or active assignment. Review the resource boundaries.";
        if (changedFacts.Contains(activation.Leg.Id))
          changes.Add(
            activation with
            {
              Status = activation.Leg.Status,
              ReviewReason = reason,
            }
          );
        else
          Observe(activation.Leg, reason);
      }
    }
    await ExecutionAcceptance.ApplyAsync(
      db,
      changes,
      "source-synchronized",
      null,
      null,
      now,
      ct
    );
    await ExecutionAcceptance.ObserveAsync(
      db,
      reviews.Values.ToArray(),
      now,
      ct
    );
    return changes
      .Select(x => x.Leg)
      .Concat(reviews.Values.Select(x => x.Leg))
      .DistinctBy(x => x.Id)
      .ToArray();

    void Observe(ExecutionLeg leg, string? reason)
    {
      if (leg.SourceReviewReason != reason)
        reviews[leg.Id] = new(leg, reason);
    }

    static ExecutionAssignment Assignment(ExecutionLeg leg) =>
      new(leg.TruckId, leg.DriverId, leg.TrailerId, leg.CoDriverId);

    List<DispatchStop> ReadSnapshot(ExecutionLeg leg)
    {
      if (!snapshots.TryGetValue(leg.Id, out var stops))
      {
        stops = ExecutionStopRows.Read(leg);
        snapshots.Add(leg.Id, stops);
      }
      return stops;
    }
  }
}
