using Application.Features.Execution.Models;
using Application.Features.Execution.Services;
using Application.Features.Mileage.Models;
using Domain.Entities.Mileage;

namespace Application.Features.Mileage.Services;

// Turning staged odometer intervals into recorded mileage. Every interval
// arrives pending and leaves one of two ways: recorded against the leg and
// cargo state it was driven under, or written down as a gap naming what
// could not be established. Nothing is dropped silently - an interval the
// system cannot explain becomes a gap a person can read.
public sealed partial class AutomaticMileageRecorder
{
  // Why an interval cannot become recorded mileage. Each answer names the
  // fact that is missing, not the step that failed: a person reading the
  // gap should learn what to go and confirm.
  private static string? Rejection(
    int matchingLegs,
    bool movementReadTruncated,
    MileageSegmentScope? scope
  ) =>
    matchingLegs > 1 ? "ambiguous-native-assignment"
    : movementReadTruncated ? "movement-overlap-read-limit"
    : scope is null ? "unconfirmed-assignment-or-cargo-boundary"
    : scope.CargoState == "unknown" ? "unconfirmed-cargo-state"
    : null;

  private async Task DrainIntervalsAsync(DateTime now, CancellationToken ct)
  {
    var intervals = await db
      .OdometerIntervals.Where(x => x.Status == "pending")
      .OrderBy(x => x.CheckedAt)
      .ThenBy(x => x.EndedAt)
      .Take(MaximumPendingBatch)
      .ToListAsync(ct);
    if (intervals.Count == 0)
      return;
    var truckIds = intervals.Select(x => x.TruckId).Distinct().ToArray();
    var start = intervals.Min(x => x.StartedAt);
    var end = intervals.Max(x => x.EndedAt);
    var legs = await db
      .ExecutionLegs.AsNoTracking()
      .Include(x => x.Loads)
      .Where(x =>
        truckIds.Contains(x.TruckId)
        && x.RecordedAt <= end
        && (x.CompletedAt == null || x.CompletedAt >= start)
        && (x.Status == "active" || x.Status == "completed")
      )
      .Take(2001)
      .ToListAsync(ct);
    if (legs.Count > 2000)
      legs.Clear();
    var legIds = legs.Select(x => x.Id).ToArray();
    var stops = legs.ToDictionary(
      x => x.Id,
      x => ExecutionStopRows.Read(x).ToArray()
    );
    var transfers = await db
      .SwitchParticipants.AsNoTracking()
      .Where(x =>
        !x.IsCancelled
        && (
          legIds.Contains(x.OutgoingLegId) || legIds.Contains(x.IncomingLegId)
        )
      )
      .ToListAsync(ct);
    var visits = ExecutionTransfers.Project(legs, transfers);
    foreach (var leg in legs)
    {
      foreach (var stop in stops[leg.Id])
        if (visits.TryGetValue(stop.Id, out var visit))
          ExecutionSnapshots.ApplyActual(stop, visit);
      foreach (var transfer in transfers)
      {
        if (transfer.OutgoingLegId == leg.Id && transfer.ReleasedAt is null)
          foreach (
            var stop in stops[leg.Id]
              .Where(x => x.Id == transfer.ReleaseVisitId)
          )
            stop.AwaitingHandoff = true;
        if (transfer.IncomingLegId == leg.Id && transfer.ReceivedAt is null)
          foreach (
            var stop in stops[leg.Id]
              .Where(x => x.Id == transfer.ReceiveVisitId)
          )
            stop.AwaitingHandoff = true;
      }
    }
    var existing = await db
      .Movements.AsNoTracking()
      .Where(x =>
        truckIds.Contains(x.TruckId)
        && x.StartedAt != null
        && x.StartedAt < end
        && (x.EndedAt == null || x.EndedAt > start)
      )
      .Take(10_001)
      .ToListAsync(ct);
    var policy = await PolicyAsync(ct);
    var gaps = new Dictionary<Guid, MileageCaptureGap>();
    var ready =
      new List<(OdometerInterval Interval, MileageSegmentScope Scope)>();
    foreach (var interval in intervals)
    {
      interval.CheckedAt = now;
      var matches = legs.Where(x => x.TruckId == interval.TruckId)
        .Select(x =>
          (
            Leg: x,
            Index: OdometerEvidence.Segment(
              x,
              stops[x.Id],
              interval.StartedAt,
              interval.EndedAt
            )
          )
        )
        .Where(x => x.Index >= 0)
        .Take(2)
        .ToArray();
      var scope =
        matches.Length == 1
          ? MileageSegmentScope.Create(
            matches[0].Leg,
            stops[matches[0].Leg.Id],
            matches[0].Index
          )
          : null;
      var error = Rejection(matches.Length, existing.Count > 10_000, scope);
      if (error is not null)
      {
        if (interval.RecordedAt + PendingRetention > now)
          continue;
        interval.GapId = Gap(
          gaps,
          interval.TruckId,
          scope?.Leg.Id,
          interval.StartedAt,
          interval.EndedAt,
          error,
          now
        );
        interval.Status = "gap";
        continue;
      }
      if (
        existing.Any(x =>
          x.TruckId == interval.TruckId
          && x.StartedAt < interval.EndedAt
          && (x.EndedAt == null || x.EndedAt > interval.StartedAt)
        )
      )
      {
        interval.Status = "gap";
        interval.GapId = Gap(
          gaps,
          interval.TruckId,
          scope!.Leg.Id,
          interval.StartedAt,
          interval.EndedAt,
          "existing-physical-movement",
          now
        );
        continue;
      }
      ready.Add((interval, scope!));
    }
    var locked = new HashSet<Guid>();
    foreach (
      var leg in ready
        .Select(x => x.Scope.Leg)
        .DistinctBy(x => x.Id)
        .OrderBy(x => x.Id)
    )
      if (await db.LockExecutionLegAsync(leg.Id, leg.Revision, ct))
        locked.Add(leg.Id);
    foreach (
      var group in ready
        .Where(x => locked.Contains(x.Scope.Leg.Id))
        .GroupBy(x => x.Scope.Identity("samsara-obd"))
    )
    {
      var ordered = group.OrderBy(x => x.Interval.StartedAt).ToArray();
      var batch = new List<OdometerInterval>();
      foreach (var item in ordered)
      {
        if (
          batch.Count > 0
          && (
            batch[^1].EndedAt != item.Interval.StartedAt
            || batch[^1].EndMeters != item.Interval.StartMeters
          )
        )
        {
          RecordObserved(batch, item.Scope, existing, policy, now, gaps);
          batch.Clear();
        }
        batch.Add(item.Interval);
      }
      RecordObserved(batch, ordered[0].Scope, existing, policy, now, gaps);
    }
    await SaveGapsAsync(gaps, ct);
  }

  private void RecordObserved(
    IReadOnlyList<OdometerInterval> intervals,
    MileageSegmentScope scope,
    List<Movement> existing,
    MileageAllocationPolicy policy,
    DateTime now,
    Dictionary<Guid, MileageCaptureGap> gaps
  )
  {
    var first = intervals[0];
    var last = intervals[^1];
    if (
      existing.Any(x =>
        x.TruckId == first.TruckId
        && x.StartedAt < last.EndedAt
        && (x.EndedAt == null || x.EndedAt > first.StartedAt)
      )
    )
    {
      foreach (var interval in intervals)
      {
        interval.Status = "gap";
        interval.GapId = Gap(
          gaps,
          first.TruckId,
          scope.Leg.Id,
          interval.StartedAt,
          interval.EndedAt,
          "existing-physical-movement",
          now
        );
      }
      return;
    }
    var identity =
      $"{scope.Identity("samsara-obd")}:"
      + $"{first.StartedAt.Ticks}:{last.EndedAt.Ticks}";
    var movement = scope.Movement(
      "samsara-obd",
      Key(identity),
      Hash(identity),
      now,
      first.StartedAt,
      last.EndedAt
    );
    var evidence = new MovementDistanceEvidence
    {
      Id = Guid.NewGuid(),
      MovementId = movement.Id,
      Revision = movement.Revision,
      Basis = "actual",
      Miles = OdometerEvidence.Miles(first.StartMeters, last.EndMeters),
      Source = "samsara-obd-odometer",
      SourceReference =
        $"{first.ExternalTruckId}:{first.StartedAt:O}:{last.EndedAt:O}",
      Reason = "measured-within-confirmed-native-segment",
      StartedAt = first.StartedAt,
      EndedAt = last.EndedAt,
      StartOdometerMeters = first.StartMeters,
      EndOdometerMeters = last.EndMeters,
      ObservedAt = last.EndedAt,
      RecordedAt = now,
      RecordedBy = Guid.Empty,
    };
    movement.ActualMiles = evidence.Miles;
    movement.ActualAt = evidence.ObservedAt;
    movement.ActualSource = evidence.Source;
    movement.ActualSourceReference = evidence.SourceReference;
    movement.ActualEvidenceId = evidence.Id;
    db.Movements.Add(movement);
    db.MovementDistanceEvidence.Add(evidence);
    db.MovementAllocationEvents.Add(
      MileageMutation.Allocate(
        movement,
        MileageAllocation.Resolve(movement, policy),
        policy.Revision,
        false,
        Guid.Empty,
        now
      )
    );
    existing.Add(movement);
    foreach (var interval in intervals)
    {
      interval.Status = "recorded";
      interval.MovementId = movement.Id;
    }
  }

  private static Guid Gap(
    Dictionary<Guid, MileageCaptureGap> gaps,
    Guid truckId,
    Guid? legId,
    DateTime start,
    DateTime end,
    string reason,
    DateTime now
  )
  {
    var id = Key($"gap:{truckId}:{start.Ticks}:{end.Ticks}:{reason}");
    gaps.TryAdd(
      id,
      new()
      {
        Id = id,
        TruckId = truckId,
        ExecutionLegId = legId,
        StartedAt = start,
        EndedAt = end,
        Reason = reason,
        RecordedAt = now,
      }
    );
    return id;
  }

  private async Task SaveGapsAsync(
    Dictionary<Guid, MileageCaptureGap> gaps,
    CancellationToken ct
  )
  {
    if (gaps.Count == 0)
      return;
    var ids = gaps.Keys.ToArray();
    var existing = await db
      .MileageCaptureGaps.AsNoTracking()
      .Where(x => ids.Contains(x.Id))
      .Select(x => x.Id)
      .ToListAsync(ct);
    foreach (var gap in gaps.Values.Where(x => !existing.Contains(x.Id)))
      db.MileageCaptureGaps.Add(gap);
  }
}
