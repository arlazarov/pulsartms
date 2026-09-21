using Application.Features.Mileage.Services;
using Domain.Entities.Dispatch;
using Domain.Entities.Execution;
using Domain.Models.Execution;

namespace Application.Features.Execution.Services;

public sealed record ExecutionChange(
  ExecutionLeg Leg,
  IReadOnlyList<DispatchStop> Stops
)
{
  public Guid? PreviousTruckId { get; init; }
  public bool UpdateBoundaries { get; init; }
  public bool SupersedePlannedMileage { get; init; }
  public IReadOnlyDictionary<Guid, Guid?> SourceReferences { get; init; } =
    new Dictionary<Guid, Guid?>();
  public string SourceSignature { get; init; } = Leg.SourceSignature;
  public string? ReviewReason { get; init; } = Leg.SourceReviewReason;
  public DateTime? CompletedAt { get; init; }
  public string? Status { get; init; }
}

public sealed record ExecutionReviewObservation(
  ExecutionLeg Leg,
  string? Reason
);

public static class ExecutionAcceptance
{
  public static async Task ApplyAsync(
    IAppDbContext db,
    IReadOnlyCollection<ExecutionChange> changes,
    string operation,
    Guid? actor,
    Guid? correlationId,
    DateTime now,
    CancellationToken ct
  )
  {
    if (db.Database.CurrentTransaction is null)
      throw new InvalidOperationException(
        "Accepted execution changes require the owning transaction."
      );
    if (changes.Select(x => x.Leg.Id).Distinct().Count() != changes.Count)
      throw new InvalidOperationException(
        "An assignment may be accepted only once in a change set."
      );
    foreach (var change in changes.OrderBy(x => x.Leg.Id))
    {
      if (
        change.Stops.Count is < 1 or > 49
        || change.Stops.Any(x => x.Id == Guid.Empty)
        || change.Stops.Select(x => x.Id).Distinct().Count()
          != change.Stops.Count
        || change.UpdateBoundaries && change.Leg.Loads.Count != 1
        || change.SourceReferences.Keys.Any(id =>
          !change.Stops.Any(stop => stop.Id == id)
        )
        || change.Leg.Revision is < 1 or long.MaxValue
      )
        throw new InvalidOperationException("Invalid accepted execution.");
      if (db.Entry(change.Leg).State == EntityState.Added)
      {
        if (change.Leg.Revision != 1)
          throw new InvalidOperationException(
            "New execution must start at revision one."
          );
        continue;
      }
      if (
        !await db.LockExecutionLegAsync(change.Leg.Id, change.Leg.Revision, ct)
      )
        throw new DbUpdateConcurrencyException(
          "Execution changed before its stops could be accepted."
        );
      if (await HasProtectedPathAsync(db, change.Leg, change.Stops, ct))
        throw new DbUpdateConcurrencyException(
          "Recorded mileage protects the accepted execution path."
        );
    }
    foreach (var change in changes)
    {
      var leg = change.Leg;
      var before = ExecutionStopRows.Read(leg);
      var affected = ExecutionStopPaths.Compare(before, change.Stops);
      if (
        db.Entry(leg).State != EntityState.Added
        && (affected.HasChanges || change.SupersedePlannedMileage)
      )
        await SupersedeAsync(
          db,
          leg.Id,
          affected,
          change.SupersedePlannedMileage,
          actor,
          now,
          ct
        );
      ExecutionStopRows.Replace(leg, change.Stops);
      foreach (var (visit, source) in change.SourceReferences)
        leg.Stops.Single(x => x.Id == visit).SourceDispatchStopId = source;
      if (change.UpdateBoundaries)
      {
        var link = leg.Loads.Single();
        link.StartVisitId = change.Stops[0].Id;
        link.EndVisitId = change.Stops[^1].Id;
      }
      leg.SourceSignature = change.SourceSignature;
      leg.SourceReviewReason = change.ReviewReason;
      if (change.Status is { } status)
      {
        leg.Status = status;
        leg.CompletedAt = change.CompletedAt;
      }
      else if (change.CompletedAt is { } completed)
      {
        leg.Status = "completed";
        leg.CompletedAt = completed;
      }
      if (db.Entry(leg).State != EntityState.Added)
        leg.Revision++;
    }
    var legs = changes.Select(x => x.Leg).ToArray();
    ExecutionPlanningChanges.Enqueue(
      db,
      legs,
      now,
      changes
        .Where(x => x.PreviousTruckId.HasValue)
        .ToDictionary(x => x.Leg.Id, x => x.PreviousTruckId!.Value)
    );
    await ExecutionHistory.RecordAsync(
      db,
      legs,
      operation,
      actor,
      correlationId,
      now,
      ct
    );
  }

  public static async Task ObserveAsync(
    IAppDbContext db,
    IReadOnlyCollection<ExecutionReviewObservation> observations,
    DateTime now,
    CancellationToken ct
  )
  {
    if (db.Database.CurrentTransaction is null)
      throw new InvalidOperationException(
        "Source review requires the owning import transaction."
      );
    foreach (var observation in observations.OrderBy(x => x.Leg.Id))
    {
      var leg = observation.Leg;
      if (!await db.LockExecutionLegAsync(leg.Id, leg.Revision, ct))
        throw new DbUpdateConcurrencyException(
          "Execution changed before source review could be recorded."
        );
      leg.SourceReviewReason = observation.Reason;
    }
    ExecutionPlanningChanges.Enqueue(db, observations.Select(x => x.Leg), now);
  }

  public static async Task<bool> HasProtectedPathAsync(
    IAppDbContext db,
    ExecutionLeg leg,
    IReadOnlyList<DispatchStop> stops,
    CancellationToken ct
  )
  {
    var affected = ExecutionStopPaths.Compare(
      ExecutionStopRows.Read(leg),
      stops
    );
    if (!affected.HasChanges)
      return false;
    var protectedPaths = await db
      .Movements.Where(x => x.ExecutionLegId == leg.Id)
      .Where(x =>
        x.Origin != "native-route"
        || x.ManualOverride
        || x.StartedAt != null
        || x.EndedAt != null
        || x.ActualEvidenceId != null
        || x.ActualMiles != null
        || db.MovementDistanceEvidence.Any(e =>
          e.MovementId == x.Id && e.RecordedBy != Guid.Empty
        )
        || db.MovementAllocationEvents.Any(e =>
          e.MovementId == x.Id
          && e.RecordedBy != Guid.Empty
          && e.Reason != "planned-segment-superseded"
        )
      )
      .Select(x => new { x.FromVisitId, x.ToVisitId })
      .ToListAsync(ct);
    return protectedPaths.Any(x =>
      affected.Affects(x.FromVisitId, x.ToVisitId)
    );
  }

  private static async Task SupersedeAsync(
    IAppDbContext db,
    Guid legId,
    ExecutionStopPaths affected,
    bool all,
    Guid? actor,
    DateTime now,
    CancellationToken ct
  )
  {
    var planned = await db
      .Movements.Where(x =>
        x.ExecutionLegId == legId
        && x.Origin == "native-route"
        && !x.PlannedSuperseded
        && !x.ManualOverride
        && x.StartedAt == null
        && x.EndedAt == null
        && x.ActualEvidenceId == null
        && x.ActualMiles == null
        && !db.MovementDistanceEvidence.Any(e =>
          e.MovementId == x.Id && e.RecordedBy != Guid.Empty
        )
        && !db.MovementAllocationEvents.Any(e =>
          e.MovementId == x.Id
          && e.RecordedBy != Guid.Empty
          && e.Reason != "planned-segment-superseded"
        )
      )
      .ToListAsync(ct);
    foreach (
      var movement in planned.Where(x =>
        all || affected.Affects(x.FromVisitId, x.ToVisitId)
      )
    )
    {
      movement.PlannedSuperseded = true;
      movement.Revision++;
      db.MovementAllocationEvents.Add(
        MileageMutation.Allocate(
          movement,
          new(
            movement.AllocatedDispatchId,
            movement.AllocationTarget,
            "planned-segment-superseded"
          ),
          movement.PolicyRevision,
          movement.ManualOverride,
          actor ?? Guid.Empty,
          now
        )
      );
    }
  }
}
