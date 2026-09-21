using System.Data;
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
}
