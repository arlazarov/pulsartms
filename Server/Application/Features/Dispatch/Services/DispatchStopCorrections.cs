using Application.Features.Dispatch.Models;
using Application.Features.Execution.Models;
using Application.Features.Execution.Services;
using Domain.Entities.Dispatch;
using Domain.Entities.Execution;
using Domain.Models.Execution;

namespace Application.Features.Dispatch.Services;

public static class DispatchStopCorrections
{
  public static async Task<string?> ValidateAssignmentAsync(
    IAppDbContext db,
    StopCorrectionRequest request,
    ExecutionLeg? leg,
    CancellationToken ct,
    bool coordinatedTrailer = false
  )
  {
    if (!request.ChangeAssignment)
      return null;
    if (
      request.TruckId is not { } truck
      || truck == Guid.Empty
      || !await db.Trucks.AnyAsync(x => x.Id == truck, ct)
      || request.TrailerId is { } trailer
        && !await db.Trailers.AnyAsync(x => x.Id == trailer, ct)
      || request.DriverId is { } driver
        && !await db.Drivers.AnyAsync(x => x.Id == driver, ct)
      || request.CoDriverId is { } coDriver
        && !await db.Drivers.AnyAsync(x => x.Id == coDriver, ct)
      || request.DriverId.HasValue && request.DriverId == request.CoDriverId
    )
      return "Select existing resources; driver and co-driver must differ.";
    if (
      leg is { Status: "active" }
      && await db.ExecutionLegs.AnyAsync(
        x =>
          x.Id != leg.Id
          && x.Status == "active"
          && (
            x.TruckId == truck
            || request.TrailerId != null && x.TrailerId == request.TrailerId
            || request.DriverId != null
              && (
                (
                  x.DriverId == request.DriverId
                  || x.CoDriverId == request.DriverId
                ) && (!x.Stops.Any() || x.Stops.Any(s => !s.HasDriverOverride))
                || x.Stops.Any(s =>
                  s.HasDriverOverride
                  && (
                    s.DriverId == request.DriverId
                    || s.CoDriverId == request.DriverId
                  )
                )
              )
            || request.CoDriverId != null
              && (
                (
                  x.DriverId == request.CoDriverId
                  || x.CoDriverId == request.CoDriverId
                ) && (!x.Stops.Any() || x.Stops.Any(s => !s.HasDriverOverride))
                || x.Stops.Any(s =>
                  s.HasDriverOverride
                  && (
                    s.DriverId == request.CoDriverId
                    || s.CoDriverId == request.CoDriverId
                  )
                )
              )
          ),
        ct
      )
    )
      return "A selected resource is working on another active assignment.";
    if (
      leg is not null
      && !coordinatedTrailer
      && leg.TrailerId != request.TrailerId
      && await db.TrailerCustodyIntervals.AnyAsync(
        x =>
          db.SwitchParticipants.Any(p =>
            p.Id == x.ParticipantId
            && !p.IsCancelled
            && (p.IncomingLegId == leg.Id || p.OutgoingLegId == leg.Id)
          ),
        ct
      )
    )
      return "This trailer has recorded drop/hook custody. Correct the transfer together with its assignments.";
    return null;
  }

  public static async Task<IReadOnlyList<DispatchStop>> ApplyAsync(
    IAppDbContext db,
    DispatchWorkspaceState state,
    DispatchStop selected,
    ExecutionLeg? leg,
    StopCorrectionRequest request,
    Guid actor,
    string actorName,
    DateTime now,
    CancellationToken ct,
    IReadOnlySet<Guid>? driverStops = null,
    IReadOnlySet<Guid>? completionStops = null
  )
  {
    var previousStatus = leg?.Status;
    var affected = leg is null
      ? state.Load.Stops.ToList()
      : ExecutionStopRows.Read(leg);
    if (request.ChangeAssignment)
    {
      var truck = await db.Trucks.SingleAsync(x => x.Id == request.TruckId, ct);
      var trailer = request.TrailerId is { } trailerId
        ? await db.Trailers.SingleAsync(x => x.Id == trailerId, ct)
        : null;
      var driver = request.DriverId is { } driverId
        ? await db.Drivers.SingleAsync(x => x.Id == driverId, ct)
        : null;
      var coDriver = request.CoDriverId is { } coDriverId
        ? await db.Drivers.SingleAsync(x => x.Id == coDriverId, ct)
        : null;
      foreach (var stop in affected)
      {
        if (driverStops?.Contains(stop.Id) == true)
          stop.HasDriverOverride = true;
        if (request.ChangeTruck != false)
        {
          stop.TruckId = truck.Id;
          stop.TruckNumber = truck.UnitNumber;
        }
        if (request.ChangeTrailer != false)
        {
          stop.TrailerId = trailer?.Id;
          stop.TrailerNumber = trailer?.UnitNumber ?? "";
        }
        if (
          request.ChangeDriver != false
          && (driverStops is null || driverStops.Contains(stop.Id))
        )
        {
          stop.DriverId = driver?.Id;
          stop.DriverName = driver?.Name ?? "";
        }
        if (
          request.ChangeCoDriver != false
          && (driverStops is null || driverStops.Contains(stop.Id))
        )
        {
          stop.CoDriverId = coDriver?.Id;
          stop.CoDriverName = coDriver?.Name ?? "";
        }
      }
      if (leg is null)
      {
        if (request.ChangeTruck != false)
        {
          state.Load.TruckId = truck.Id;
          state.Load.TruckNumber = truck.UnitNumber;
          state.Load.PlanningTruckId = truck.Id;
          state.Load.PlanningFromStopId ??= affected
            .OrderBy(x => x.Sequence)
            .First()
            .Id;
        }
        if (request.ChangeTrailer != false)
        {
          state.Load.TrailerId = trailer?.Id;
          state.Load.TrailerNumber = trailer?.UnitNumber ?? "";
        }
        if (request.ChangeDriver != false && driverStops is null)
        {
          state.Load.DriverId = driver?.Id;
          state.Load.DriverName = driver?.Name ?? "";
        }
        state.Load.PlanningAssignmentRevision++;
        if (state.Load.Status is "unassigned" or "planned")
          state.Load.Status = "assigned";
      }
      else
      {
        leg.TruckId = truck.Id;
        leg.TrailerId = trailer?.Id;
        if (driverStops is null)
        {
          leg.DriverId = driver?.Id;
          leg.CoDriverId = coDriver?.Id;
        }
        // Odometer evidence remains tied to the equipment that produced it.
        if (
          await db.Movements.AnyAsync(
            x =>
              x.ExecutionLegId == leg.Id
              && (x.ActualMiles != null || x.ActualEvidenceId != null),
            ct
          )
        )
          leg.SourceReviewReason =
            "Assignment corrected; review previously recorded mileage evidence.";
        var planned = await db
          .Movements.Where(x =>
            x.ExecutionLegId == leg.Id
            && !x.PlannedSuperseded
            && x.StartedAt == null
            && x.EndedAt == null
            && x.ActualMiles == null
            && x.ActualEvidenceId == null
            && !x.ManualOverride
            && x.Origin == "native-route"
          )
          .ToListAsync(ct);
        foreach (var movement in planned)
        {
          movement.PlannedSuperseded = true;
          movement.Revision++;
        }
      }
    }
    if (request.Completion != "keep")
    {
      var targets = completionStops is null
        ? affected.Where(x => x.Id == selected.Id)
        : affected.Where(x => completionStops.Contains(x.Id));
      foreach (var target in targets)
      {
        Complete(target);
        if (
          leg is not null
          && state.Load.Stops.SingleOrDefault(x => x.Id == target.Id)
            is { } source
        )
          Complete(source);
      }
      if (request.Completion == "pending")
      {
        if (
          state.Load.Status.Equals(
            "completed",
            StringComparison.OrdinalIgnoreCase
          )
        )
          state.Load.Status = "in_transit";
        // Reopening cargo work cannot undo a confirmed release to another leg.
        if (leg is { Status: "completed", EndSwitchId: null })
        {
          leg.Status = "active";
          leg.CompletedAt = null;
        }
      }
    }
    if (leg is not null)
    {
      var transfers = await ExecutionTransfers.ReadAsync(db, [leg], ct);
      ExecutionLegProgress.Apply(
        leg,
        affected,
        transfers
          .Values.Where(x => x.ConfirmedBy.HasValue)
          .Select(x => x.Id)
          .ToHashSet()
      );
      if (
        previousStatus != "active"
        && leg.Status == "active"
        && await ExecutionResources.ConflictsAsync(
          db,
          new(leg.TruckId, leg.DriverId, leg.TrailerId, leg.CoDriverId),
          null,
          ct,
          leg.Id
        )
      )
        throw new DbUpdateConcurrencyException(
          "A resource is already active or awaiting a transfer receipt."
        );
    }
    return affected;

    void Complete(DispatchStop stop)
    {
      stop.CompletionOverride = request.Completion == "completed";
      stop.ManualCompletedAt =
        stop.CompletionOverride == true
          ? stop.Id == selected.Id
            ? request.CompletedAt?.UtcDateTime
            : null
          : null;
      stop.ManualCompletionRevision++;
      stop.ManualCompletedBy = actor;
      stop.ManualCompletedByName = actorName;
      stop.ManualCompletionRecordedAt = now;
    }
  }
}
