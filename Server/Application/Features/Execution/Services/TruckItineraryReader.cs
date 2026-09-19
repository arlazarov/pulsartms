using System.Collections.Immutable;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using Application.Diagnostics;
using Application.Features.Execution.Interfaces;
using Application.Features.Execution.Models;
using Application.Features.Routing.Models;
using Application.Features.Routing.Services.Routes;
using Domain.Entities.Dispatch;
using Domain.Entities.Execution;

namespace Application.Features.Execution.Services;

public sealed class TruckItineraryReader(
  IAppDbContext db,
  IExecutionReadScope scope
)
{
  public async Task<TruckItinerarySnapshot?> ReadAsync(
    Guid truckId,
    DateTimeOffset asOf,
    CancellationToken ct
  ) => (await ReadManyAsync([truckId], asOf, ct)).GetValueOrDefault(truckId);

  public Task<IReadOnlyDictionary<Guid, TruckItinerarySnapshot>> ReadManyAsync(
    IReadOnlyCollection<Guid> truckIds,
    DateTimeOffset asOf,
    CancellationToken ct
  ) => scope.ReadAsync(token => ReadCoreAsync(truckIds, asOf, token), ct);

  public async Task<bool> MatchesAsync(
    TruckItinerarySnapshot previous,
    CancellationToken ct
  ) =>
    (
      await scope.ReadAsync(
        token => ReadCoreAsync([previous.TruckId], previous.AsOf, token),
        ct,
        requireFreshSnapshot: true
      )
    ).GetValueOrDefault(previous.TruckId)
      is { } current
    && current.InputSignature == previous.InputSignature;

  private async Task<
    IReadOnlyDictionary<Guid, TruckItinerarySnapshot>
  > ReadCoreAsync(
    IReadOnlyCollection<Guid> truckIds,
    DateTimeOffset asOf,
    CancellationToken ct
  )
  {
    if (truckIds.Count == 0)
      return new Dictionary<Guid, TruckItinerarySnapshot>();
    var day = DateOnly.FromDateTime(asOf.UtcDateTime);
    var at = Stopwatch.GetTimestamp();
    var batch = await ExecutionWorkReader.ReadBatchAsync(
      db,
      day,
      null,
      includePlanned: true,
      includeOverdue: true,
      ct,
      includeInactive: true,
      truckIds: truckIds
    );
    PerformanceStages.Elapsed("itinerary-read", "work-batch", at);
    at = Stopwatch.GetTimestamp();
    var work = batch.Rows.SelectMany(x => x.Loads).ToArray();
    var legacyIds = work.Where(x => !x.ExecutionLegId.HasValue)
      .Select(x => x.Id)
      .Distinct()
      .ToArray();
    var legacy = await db
      .Dispatches.AsNoTracking()
      .Include(x => x.Stops)
      .Where(x => legacyIds.Contains(x.Id))
      .Select(x => new
      {
        Load = x,
        Source = db.DispatchSourceLinks.FirstOrDefault(s =>
          s.DispatchId == x.Id
        ),
      })
      .ToDictionaryAsync(x => x.Load.Id, ct);
    PerformanceStages.Elapsed("itinerary-read", "legacy", at);
    at = Stopwatch.GetTimestamp();
    var native = batch
      .Native.Loads.Select(x => x.Work)
      .ToDictionary(x => new WorkIdentity(x.Id, x.ExecutionLegId));
    var legs = batch.Native.Legs.ToDictionary(x => x.Id);
    var evidence = await WorkSequenceReader.ReadAsync(db, work, ct);
    PerformanceStages.Elapsed("itinerary-read", "evidence", at);
    at = Stopwatch.GetTimestamp();
    var result = new Dictionary<Guid, TruckItinerarySnapshot>();
    foreach (var row in batch.Rows)
    {
      if (
        row.TruckId is not { } truckId
        || !batch.Resources.TryGetValue(truckId, out var resources)
      )
        continue;
      var nativeIds = row
        .Loads.Where(x => x.ExecutionLegId.HasValue)
        .Select(x => x.ExecutionLegId!.Value)
        .ToHashSet();
      var dispatchIds = row
        .Loads.Where(x => x.ExecutionLegId.HasValue)
        .Select(x => x.Id)
        .ToHashSet();
      var truckEvidence = new WorkSequenceEvidence(
        evidence
          .Legs.Where(x => dispatchIds.Contains(x.DispatchId))
          .ToImmutableArray(),
        evidence
          .Transfers.Where(x =>
            nativeIds.Contains(x.OutgoingLegId)
            || nativeIds.Contains(x.IncomingLegId)
          )
          .ToImmutableArray()
      );
      var loads = new List<RouteWorkSnapshot>();
      var segments = ImmutableArray.CreateBuilder<TruckWorkSegment>();
      foreach (var item in row.Loads)
      {
        var source = item.ExecutionLegId.HasValue
          ? native[new(item.Id, item.ExecutionLegId)]
          : RouteWorkProjection.Capture(legacy[item.Id].Load);
        var path = RouteWorkProjection.TruckItinerary(source);
        loads.Add(path);
        segments.Add(
          Segment(
            row,
            item,
            source,
            path,
            item.ExecutionLegId is { } id ? legs[id] : null,
            batch.Native.Visits,
            truckEvidence.Transfers,
            day,
            legacy.GetValueOrDefault(item.Id)?.Source is { } import
              ? import.ExecutionReviewReason
                ?? "Source execution has not been accepted."
              : null
          )
        );
      }
      var sequence = WorkSequencePolicy.Assess(loads, truckEvidence);
      var values = segments.ToImmutable();
      var signature = Convert.ToHexString(
        SHA256.HashData(
          JsonSerializer.SerializeToUtf8Bytes(
            new
            {
              Policy = 3,
              TruckId = truckId,
              resources,
              Segments = values,
              Evidence = truckEvidence,
              sequence,
            }
          )
        )
      );
      result[truckId] = new(
        truckId,
        asOf.ToUniversalTime(),
        signature,
        resources,
        values,
        truckEvidence,
        sequence
      );
    }
    PerformanceStages.Elapsed("itinerary-read", "assemble", at);
    return result;
  }

  private static TruckWorkSegment Segment(
    TruckWorkSelection truck,
    WorkLoadReference reference,
    RouteWorkSnapshot source,
    RouteWorkSnapshot path,
    ExecutionLeg? leg,
    IReadOnlyDictionary<Guid, ExecutionTransferVisit> nativeVisits,
    IReadOnlyList<WorkTransferDependency> transfers,
    DateOnly day,
    string? importReview
  )
  {
    var problems = ImmutableArray.CreateBuilder<WorkReadProblem>();
    if (source.Stops.Length == 0)
      problems.Add(WorkReadProblem.MissingVisits);
    else if (path.Stops.Length == 0)
      problems.Add(WorkReadProblem.UnresolvedTruckPath);
    var assigned = path.TruckId;
    if (
      !assigned.HasValue
      && path.TruckNumber.Trim()
        .Equals(truck.TruckNumber.Trim(), StringComparison.OrdinalIgnoreCase)
    )
      assigned = truck.TruckId;
    if (
      assigned != truck.TruckId
      || path.Stops.Any(x =>
        x.TruckId.HasValue && x.TruckId != truck.TruckId
        || !string.IsNullOrWhiteSpace(x.TruckNumber)
          && !x
            .TruckNumber.Trim()
            .Equals(
              truck.TruckNumber.Trim(),
              StringComparison.OrdinalIgnoreCase
            )
      )
    )
      problems.Add(WorkReadProblem.ConflictingAssignment);
    if (leg?.SourceReviewReason is not null || importReview is not null)
      problems.Add(WorkReadProblem.SourceReviewRequired);
    if (
      leg?.StartSwitchId is { } start
        && !transfers.Any(x =>
          x.SwitchId == start
          && x.IncomingLegId == leg.Id
          && x.ReceiveVisitId == source.Stops.FirstOrDefault()?.Id
        )
      || leg?.EndSwitchId is { } endSwitch
        && !transfers.Any(x =>
          x.SwitchId == endSwitch
          && x.OutgoingLegId == leg.Id
          && x.ReleaseVisitId == source.Stops.LastOrDefault()?.Id
        )
    )
      problems.Add(WorkReadProblem.MissingTransfer);
    var pathIds = path.Stops.Select(x => x.Id).ToHashSet();
    IEnumerable<RouteWorkStop> stops = leg is null
      ? StopOperation.Resolve(
        source.Stops,
        source.PlanningFromStopId,
        (stop, job, state) => stop with { Job = job, StateAfter = state }
      )
      : source.Stops;
    var visits = stops
      .OrderBy(x => x.Sequence)
      .Select(x =>
        Visit(
          x,
          pathIds.Contains(x.Id),
          leg is null
            ? x.Id
            : nativeVisits.GetValueOrDefault(x.Id)?.SourceDispatchStopId
        )
      )
      .ToImmutableArray();
    var end = leg is null
      ? source.DeliveryDate
        ?? visits.LastOrDefault()?.Appointment.Date
        ?? source.ShipDate
      : visits.LastOrDefault()?.Appointment.Date
        ?? source.DeliveryDate
        ?? source.ShipDate;
    return new(
      new(source.Id, source.ExecutionLegId),
      source.ExecutionStatus ?? source.Status,
      source.LoadNumber,
      reference.Order,
      assigned,
      source.DriverId,
      leg?.CoDriverId,
      source.TrailerId,
      leg?.Revision ?? source.PlanningAssignmentRevision,
      source.RouteChoiceRevision,
      source.ShipDate,
      source.DeliveryDate,
      reference.Order.Activity == WorkActivity.Upcoming && end < day,
      leg?.SourceSignature,
      leg?.SourceObservedSignature,
      leg?.SourceReviewReason ?? importReview,
      visits,
      problems.ToImmutable()
    );
  }

  private static WorkVisitFacts Visit(
    RouteWorkStop stop,
    bool inTruckPath,
    Guid? sourceStopId
  ) =>
    new(
      stop.Id,
      sourceStopId,
      stop.Sequence,
      stop.ManualAction ?? stop.Job,
      stop.StateAfter,
      stop.ManualAction,
      stop.ManualStateAfter,
      inTruckPath,
      stop.Commodity,
      stop.Notes,
      new(
        stop.Name,
        stop.Address,
        stop.City,
        stop.Province,
        stop.Country,
        stop.ZipCode,
        stop.Latitude,
        stop.Longitude,
        stop.AddressVerifiedAt,
        stop.AddressRetryAfter,
        ReadSourceAddress(stop.SourceAddressJson)
      ),
      new(
        stop.ScheduledDate,
        stop.ScheduledTime,
        stop.ScheduledDate2,
        stop.ScheduledTime2,
        stop.IsWindow,
        stop.AppointmentTimeZoneId
      ),
      new(
        stop.ArrivedAt,
        stop.PickedUpAt,
        stop.DeliveredAt,
        stop.DepartedAt,
        stop.ManualCompletedAt,
        stop.ManualCompletedBy,
        stop.ExecutionCompleted,
        stop.CompletionOverride,
        stop.ManualCompletionRevision,
        stop.AwaitingHandoff,
        stop.IsCompleted
      ),
      stop.TruckId,
      stop.DriverId,
      stop.CoDriverId,
      stop.TrailerId,
      stop.OperationRevision
    );

  private static WorkSourceAddress? ReadSourceAddress(string json)
  {
    if (string.IsNullOrWhiteSpace(json))
      return null;
    try
    {
      return JsonSerializer.Deserialize<WorkSourceAddress>(json);
    }
    catch (JsonException)
    {
      return null;
    }
  }
}
