using Application.Features.Dispatch.Models;
using Application.Features.Dispatch.Queries;
using Application.Features.Execution.Models;
using Application.Features.Execution.Services;
using Domain.Entities.Dispatch;
using Domain.Entities.Execution;
using DispatchEntity = Domain.Entities.Dispatch.Dispatch;

namespace Application.Features.Dispatch.Services;

public sealed record DispatchWorkspaceState(
  DispatchEntity Load,
  DispatchWorkspace? Workspace,
  List<ExecutionLeg> Legs,
  Dictionary<Guid, DispatchStop> EffectiveStops,
  DispatchWorkspaceResponse Response
);

public static partial class DispatchWorkspaceReader
{
  private static IEnumerable<List<DispatchStop>> DriverSections(
    ExecutionLeg leg
  )
  {
    var section = new List<DispatchStop>();
    foreach (var stop in ExecutionStopRows.Read(leg))
    {
      if (
        section.Count > 0
        && (
          section[0].DriverId != stop.DriverId
          || section[0].CoDriverId != stop.CoDriverId
        )
      )
      {
        yield return section;
        section = [];
      }
      section.Add(stop);
    }
    if (section.Count > 0)
      yield return section;
  }

  public static async Task<DispatchWorkspaceState?> ReadAsync(
    IAppDbContext db,
    Guid id,
    bool canEdit,
    CancellationToken ct
  )
  {
    var load = await db
      .Dispatches.Include(x => x.Stops)
      .SingleOrDefaultAsync(x => x.Id == id, ct);
    if (load is null)
      return null;
    var workspace = await db.DispatchWorkspaces.SingleOrDefaultAsync(
      x => x.Id == id,
      ct
    );
    var sourceLink = await db
      .DispatchSourceLinks.AsNoTracking()
      .Where(x => x.DispatchId == id)
      .SingleOrDefaultAsync(ct);
    var links = await db
      .LoadExecutionLegs.Include(x => x.ExecutionLeg)
      .ThenInclude(x => x.Loads)
      .Where(x => x.DispatchId == id)
      .OrderBy(x => x.Sequence)
      .ToListAsync(ct);
    var legs = links
      .Select(x => x.ExecutionLeg)
      .Where(x => x.Status != "cancelled")
      .ToList();
    var legIds = legs.Select(x => x.Id).ToArray();
    List<SwitchParticipant> transfers =
      legIds.Length == 0
        ? []
        : await db
          .SwitchParticipants.AsNoTracking()
          .Where(x => x.DispatchId == id && !x.IsCancelled)
          .ToListAsync(ct);
    var extras = workspace is null
      ? new Dictionary<Guid, DispatchWorkspaceStop>()
      : DispatchWorkspaceData.Read<Dictionary<Guid, DispatchWorkspaceStop>>(
        workspace.StopExtrasJson
      );
    var metadata = workspace is null
      ? new DispatchWorkspaceMetadata()
      : DispatchWorkspaceData.Read<DispatchWorkspaceMetadata>(
        workspace.MetadataJson
      );
    metadata.OrderNumber = load.OrderNumber;
    metadata.CustomerName = load.CustomerName;
    metadata.Price = load.Price;
    metadata.Currency = load.Currency;
    var rows = new List<(DispatchStop Stop, ExecutionLeg? Leg)>();
    if (legs.Count == 0)
      rows.AddRange(
        load.Stops.OrderBy(x => x.Sequence)
          .Select(x => (x, (ExecutionLeg?)null))
      );
    else
      foreach (var leg in legs)
        rows.AddRange(
          ExecutionStopRows.Read(leg).Select(x => (x, (ExecutionLeg?)leg))
        );
    var visits = ExecutionTransfers.Project(legs, transfers);
    var protectedIds = await db
      .Movements.AsNoTracking()
      .Where(x =>
        x.ExecutionLegId.HasValue
        && legIds.Contains(x.ExecutionLegId.Value)
        && (
          x.ManualOverride
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
      )
      .Select(x => new { x.FromVisitId, x.ToVisitId })
      .ToListAsync(ct);
    var mileage = protectedIds
      .SelectMany(x => new[] { x.FromVisitId, x.ToVisitId })
      .Where(x => x.HasValue)
      .Select(x => x!.Value)
      .ToHashSet();
    var truckIds = legs.Select(x => x.TruckId).Distinct().ToArray();
    var trucks = await db
      .Trucks.AsNoTracking()
      .Where(x => truckIds.Contains(x.Id))
      .ToDictionaryAsync(x => x.Id, x => x.UnitNumber, ct);
    var driverIds = rows.SelectMany(x =>
        new[] { x.Stop.DriverId, x.Stop.CoDriverId }
      )
      .Where(x => x.HasValue)
      .Select(x => x!.Value)
      .Distinct()
      .ToArray();
    var drivers = await db
      .Drivers.AsNoTracking()
      .Where(x => driverIds.Contains(x.Id))
      .ToDictionaryAsync(x => x.Id, x => x.Name, ct);
    var trailerIds = legs.Where(x => x.TrailerId.HasValue)
      .Select(x => x.TrailerId!.Value)
      .Distinct()
      .ToArray();
    var trailers = await db
      .Trailers.AsNoTracking()
      .Where(x => trailerIds.Contains(x.Id))
      .ToDictionaryAsync(x => x.Id, x => x.UnitNumber, ct);
    var terminal =
      load.Status.ToLowerInvariant()
        is "completed"
          or "cancelled"
          or "canceled";
    var duplicate =
      rows.Select(x => x.Stop.Id).Distinct().Count() != rows.Count;
    var editable = canEdit && !terminal && !duplicate;
    var response = new DispatchWorkspaceResponse
    {
      Load = DispatchProjection.FromSource(load),
      Revision = workspace?.Revision ?? 0,
      SourceFingerprint = DispatchWorkspaceData.Fingerprint(
        load,
        legs.Select(x =>
          (
            x.Id,
            x.Revision,
            ExecutionSnapshots.Write(ExecutionStopRows.Read(x))
          )
        ),
        sourceLink?.AssignmentSignature
      ),
      Metadata = metadata,
      BillingTotals = DispatchBillingRules.Totals(metadata),
      CanEdit = editable,
      ReadOnlyReason =
        duplicate ? "Repeated execution visits need review."
        : terminal
          ? "Load fields are read-only. Completed stops still allow status and assignment corrections."
        : !canEdit ? "Dispatch access is required to edit this load."
        : null,
      LocallyManaged = workspace?.OwnsStops == true,
      SourceName = sourceLink?.DisplayName ?? "",
      SourceAssignment = string.IsNullOrEmpty(
        sourceLink?.AssignmentProposalJson
      )
        ? null
        : DispatchWorkspaceData.Read<DispatchAssignmentProposal>(
          sourceLink.AssignmentProposalJson
        ),
      AcceptedAssignments = legs.SelectMany(leg =>
          DriverSections(leg)
            .Select(ordered =>
            {
              return new DispatchAcceptedAssignment(
                leg.Id,
                leg.Revision,
                leg.Status,
                ordered.FirstOrDefault()?.Name ?? "",
                ordered.LastOrDefault()?.Name ?? "",
                new(
                  trucks.GetValueOrDefault(leg.TruckId, ""),
                  drivers.GetValueOrDefault(
                    ordered[0].DriverId ?? Guid.Empty,
                    ""
                  ),
                  drivers.GetValueOrDefault(
                    ordered[0].CoDriverId ?? Guid.Empty,
                    ""
                  ),
                  trailers.GetValueOrDefault(leg.TrailerId ?? Guid.Empty, ""),
                  leg.TruckId,
                  ordered[0].DriverId,
                  ordered[0].CoDriverId,
                  leg.TrailerId
                )
              )
              {
                FromStopId = ordered[0].Id,
                ThroughStopId = ordered[^1].Id,
              };
            })
        )
        .ToArray(),
      SourceUpdatedAt = load.LastSyncedAt == default ? null : load.LastSyncedAt,
      SourceReviewReason = ReviewReason(
        workspace,
        sourceLink,
        legs,
        links.Count == 0
          && load.PlanningAssignmentRevision > 0
          && (
            load.PlanningTruckId.HasValue
            || load.TruckId.HasValue
            || load.Stops.Any(x => x.TruckId.HasValue)
          )
      ),
      History = await db
        .DispatchWorkspaceRevisions.AsNoTracking()
        .Where(x => x.DispatchId == id)
        .OrderByDescending(x => x.Revision)
        .Take(50)
        .Select(x => new DispatchWorkspaceHistory(
          x.Revision,
          x.RecordedAt,
          x.ActorName,
          x.Summary
        ))
        .ToListAsync(ct),
    };
    var segment = 0;
    Guid? previousLeg = null;
    var effective = new Dictionary<Guid, DispatchStop>();
    foreach (var (stop, leg) in rows.DistinctBy(x => x.Stop.Id))
    {
      var source = load.Stops.SingleOrDefault(x => x.Id == stop.Id);
      if (leg is not null && source is not null)
      {
        stop.Notes = source.Notes;
        stop.StopNo = source.StopNo;
        stop.Commodity = source.Commodity;
        stop.Weight = source.Weight;
        stop.WeightUnit = source.WeightUnit;
        stop.Pallets = source.Pallets;
        stop.Pieces = source.Pieces;
        stop.Temperature = source.Temperature;
        stop.TemperatureUnit = source.TemperatureUnit;
      }
      if (leg is not null)
      {
        stop.TruckNumber = trucks.GetValueOrDefault(leg.TruckId, "");
        stop.DriverName = drivers.GetValueOrDefault(
          stop.DriverId ?? Guid.Empty,
          ""
        );
        stop.CoDriverName = drivers.GetValueOrDefault(
          stop.CoDriverId ?? Guid.Empty,
          ""
        );
        stop.TrailerNumber = trailers.GetValueOrDefault(
          leg.TrailerId ?? Guid.Empty,
          ""
        );
      }
      var locked =
        !editable ? response.ReadOnlyReason
        : leg is not null && leg.Status is not ("active" or "planned")
          ? "Recorded assignments are locked."
        : leg is not null && leg.Loads.Count != 1
          ? "Shared assignments require a coordinated change."
        : visits.ContainsKey(stop.Id)
          ? "Transfer boundaries are changed through Switch."
        : stop.IsCompleted
        || stop.ArrivedAt.HasValue
        || source?.IsCompleted == true
        || source?.ArrivedAt.HasValue == true
          ? "Recorded work cannot be moved. Use Edit status & assignment for corrections."
        : mileage.Contains(stop.Id) ? "A stop with recorded mileage is locked."
        : load.PlanningFromStopId == stop.Id
          ? "The confirmed truck starting stop is locked."
        : stop.ManualAction is not null || !Ordinary(stop.Job)
          ? "This operation requires its dedicated workflow."
        : null;
      if (locked is not null || previousLeg != leg?.Id)
        segment++;
      var row = DispatchWorkspaceData.Stop(
        stop,
        extras.GetValueOrDefault(stop.Id)
      );
      row.Sequence = response.Stops.Count + 1;
      row.ExecutionLegId = leg?.Id;
      row.CanCorrect =
        canEdit
        && !duplicate
        && load.Status.ToLowerInvariant() is not ("cancelled" or "canceled")
        && !visits.ContainsKey(stop.Id)
        && (leg is null || leg.Loads.Count == 1);
      var transfer = transfers.SingleOrDefault(x =>
        x.ReleaseVisitId == stop.Id || x.ReceiveVisitId == stop.Id
      );
      if (transfer is not null)
        row.Transfer = DispatchWorkspaceTransfers.Project(
          transfer,
          stop.Id,
          legs,
          trucks,
          trailers,
          drivers
        );
      if (row.Transfer is not null)
        row.CanCorrect =
          canEdit
          && !duplicate
          && load.Status.ToLowerInvariant() is not ("cancelled" or "canceled")
          && leg is { Loads.Count: 1 };
      row.SegmentKey = $"{leg?.Id.ToString() ?? "source"}:{segment}";
      row.CanMove = row.CanRemove = locked is null;
      row.CanEdit =
        editable
        && (leg is null || leg.Loads.Count == 1)
        && !visits.ContainsKey(stop.Id)
        && transfer is null
        && Ordinary(stop.Job)
        && stop.ManualAction is null;
      row.LockReason = locked;
      response.Stops.Add(row);
      effective.Add(stop.Id, stop);
      if (locked is not null)
        segment++;
      previousLeg = leg?.Id;
    }
    if (legs.Count > 0)
    {
      var projected = new DispatchEntity
      {
        Stops = response
          .Stops.Select(row =>
          {
            var stop = ExecutionSnapshots.Copy(effective[row.Id]);
            stop.Sequence = row.Sequence;
            if (visits.TryGetValue(stop.Id, out var visit))
              ExecutionSnapshots.ApplyActual(stop, visit);
            return stop;
          })
          .ToList(),
      };
      response.Load.Stops = DispatchProjection
        .FromExecution(ExecutionLoadProjection.Capture(projected))
        .Stops;
    }
    return new(load, workspace, legs, effective, response);
  }
}
