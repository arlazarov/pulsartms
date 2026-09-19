using System.Data;
using Application.Features.Execution.Models;
using Application.Features.Execution.Services;
using Application.Features.Mileage.Models;
using Domain.Entities.Mileage;

namespace Application.Features.Mileage.Services;

public sealed partial class AutomaticMileageRecorder
{
  private const int MaximumPageSamples = 10_000;
  private const int MaximumPendingBatch = 2_000;
  private static readonly TimeSpan PendingRetention = TimeSpan.FromDays(7);

  public Task<string?> ReadOdometerCursorAsync(CancellationToken ct) =>
    db
      .OdometerCaptureCheckpoints.AsNoTracking()
      .Where(x => x.Id == OdometerCaptureCheckpoint.SingletonId)
      .Select(x => x.Cursor)
      .SingleOrDefaultAsync(ct);

  public async Task<bool> CaptureOdometerAsync(
    string? expectedCursor,
    OdometerPage page,
    CancellationToken ct
  )
  {
    try
    {
      return await CapturePageAsync(expectedCursor, page, ct);
    }
    catch (Exception exception) when (db.IsWriteConflict(exception))
    {
      return false;
    }
  }

  private async Task<bool> CapturePageAsync(
    string? expectedCursor,
    OdometerPage page,
    CancellationToken ct
  )
  {
    if (
      page.Samples.Count > MaximumPageSamples
      || page.Cursor.Length > 4000
      || (page.Samples.Count > 0 && string.IsNullOrWhiteSpace(page.Cursor))
    )
      throw new InvalidOperationException("Odometer page exceeds its bounds.");
    await using var transaction = await db.Database.BeginTransactionAsync(
      IsolationLevel.Serializable,
      ct
    );
    var checkpoint = await db.OdometerCaptureCheckpoints.SingleOrDefaultAsync(
      x => x.Id == OdometerCaptureCheckpoint.SingletonId,
      ct
    );
    if (checkpoint?.Cursor != expectedCursor)
      return false;
    var now = clock.GetUtcNow().UtcDateTime;
    checkpoint ??= new() { Id = OdometerCaptureCheckpoint.SingletonId };
    if (db.Entry(checkpoint).State == EntityState.Detached)
      db.OdometerCaptureCheckpoints.Add(checkpoint);
    var gaps = new Dictionary<Guid, MileageCaptureGap>();
    await StageSamplesAsync(page.Samples, now, gaps, ct);
    checkpoint.Cursor = string.IsNullOrEmpty(page.Cursor) ? null : page.Cursor;
    checkpoint.UpdatedAt = now;
    checkpoint.Revision++;
    try
    {
      await SaveGapsAsync(gaps, ct);
      await db.SaveChangesAsync(ct);
      await DrainIntervalsAsync(now, ct);
      await db.SaveChangesAsync(ct);
      var cutoff = now - PendingRetention;
      var expired = await db
        .OdometerIntervals.Where(x =>
          x.Status != "pending" && x.RecordedAt < cutoff
        )
        .OrderBy(x => x.RecordedAt)
        .Take(MaximumPendingBatch)
        .ToListAsync(ct);
      db.OdometerIntervals.RemoveRange(expired);
      await db.SaveChangesAsync(ct);
      await transaction.CommitAsync(ct);
      return true;
    }
    catch (Exception exception) when (db.IsWriteConflict(exception))
    {
      // The caller discards this operation scope before retrying the cursor.
      return false;
    }
  }

  private async Task StageSamplesAsync(
    IReadOnlyList<OdometerSample> samples,
    DateTime now,
    Dictionary<Guid, MileageCaptureGap> gaps,
    CancellationToken ct
  )
  {
    var externalIds = samples
      .Select(x => x.ExternalTruckId)
      .Distinct()
      .ToArray();
    var trucks = await db
      .Trucks.AsNoTracking()
      .Where(x => externalIds.Contains(x.ExternalId))
      .Select(x => new { x.Id, x.ExternalId })
      .ToListAsync(ct);
    var truckIds = trucks.Select(x => x.Id).ToArray();
    var positions = await db
      .OdometerPositions.Where(x => truckIds.Contains(x.TruckId))
      .ToDictionaryAsync(x => x.TruckId, ct);
    var byTruck = samples.ToLookup(x => x.ExternalTruckId);
    foreach (var truckGroup in trucks.GroupBy(x => x.ExternalId))
    {
      if (truckGroup.Count() != 1)
        continue;
      var truck = truckGroup.Single();
      positions.TryGetValue(truck.Id, out var position);
      foreach (
        var reading in byTruck[truck.ExternalId]
          .OrderBy(x => x.ObservedAt)
          .GroupBy(x => x.ObservedAt)
      )
      {
        var sample = reading.First();
        var at = sample.ObservedAt.UtcDateTime;
        if (at.Year < 2000 || at > now || sample.Meters > 1_000_000_000_000m)
        {
          var anchor = position?.ObservedAt ?? now;
          Gap(
            gaps,
            truck.Id,
            null,
            anchor,
            anchor,
            "invalid-source-sample",
            now
          );
          continue;
        }
        var conflict = reading.Any(x => x.Meters != sample.Meters);
        if (position is null)
        {
          position = new()
          {
            Id = Guid.NewGuid(),
            TruckId = truck.Id,
            ExternalTruckId = truck.ExternalId,
            ObservedAt = at,
            Meters = conflict ? -1 : sample.Meters,
            Revision = 1,
          };
          db.OdometerPositions.Add(position);
          positions.Add(truck.Id, position);
          Gap(gaps, truck.Id, null, at, at, "bootstrap-anchor-only", now);
          continue;
        }
        if (at < position.ObservedAt)
          continue;
        if (at == position.ObservedAt)
        {
          if (position.Meters != sample.Meters || conflict)
          {
            Gap(
              gaps,
              truck.Id,
              null,
              at,
              at,
              "conflicting-odometer-sample",
              now
            );
            position.Meters = -1;
            position.Revision++;
          }
          continue;
        }
        var error =
          conflict ? "conflicting-odometer-sample"
          : position.ExternalTruckId != truck.ExternalId
            ? "provider-truck-id-changed"
          : OdometerEvidence.Validate(
            position.ObservedAt,
            at,
            position.Meters,
            sample.Meters
          );
        if (error is null)
          db.OdometerIntervals.Add(
            new()
            {
              Id = Key(
                $"obd:{truck.Id}:{position.ObservedAt.Ticks}:{at.Ticks}"
              ),
              TruckId = truck.Id,
              ExternalTruckId = truck.ExternalId,
              StartedAt = position.ObservedAt,
              EndedAt = at,
              StartMeters = position.Meters,
              EndMeters = sample.Meters,
              RecordedAt = now,
            }
          );
        else
          Gap(gaps, truck.Id, null, position.ObservedAt, at, error, now);
        position.ObservedAt = at;
        position.Meters = conflict ? -1 : sample.Meters;
        position.ExternalTruckId = truck.ExternalId;
        position.Revision++;
      }
    }
  }

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
      var error =
        matches.Length > 1 ? "ambiguous-native-assignment"
        : existing.Count > 10_000 ? "movement-overlap-read-limit"
        : scope is null ? "unconfirmed-assignment-or-cargo-boundary"
        : scope.CargoState == "unknown" ? "unconfirmed-cargo-state"
        : null;
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
