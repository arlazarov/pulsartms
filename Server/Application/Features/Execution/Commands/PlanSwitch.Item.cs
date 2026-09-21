using Application.Features.Execution.Models;
using Application.Features.Execution.Services;
using Application.Models;
using Domain.Entities.Dispatch;
using Domain.Entities.Execution;
using Domain.Models.Execution;
using DispatchEntity = Domain.Entities.Dispatch.Dispatch;

namespace Application.Features.Execution.Commands;

public sealed partial class PlanSwitchHandler
{
  // What the whole transfer has settled before any one load is planned:
  // the request, the loads it names, the operation being written, and the
  // three collections the loads accumulate into. Handed to each load so
  // planning one is a method with arguments rather than a block inside a
  // loop with twenty captured names.
  private sealed record PlanScope(
    PlanSwitchRequest Request,
    IReadOnlyDictionary<Guid, DispatchEntity> Loads,
    IReadOnlyDictionary<Guid, string> SourceAssignments,
    DispatchSwitchOperation Operation,
    List<ExecutionLeg> Touched,
    List<ExecutionChange> Changes,
    Dictionary<Guid, Trip> Trips,
    DateTime Now,
    Guid Actor
  );

  // Planning one load's transfer. Returns the failure to answer with, or
  // null when this load is planned.
  private async Task<RequestResponse<SwitchResult>?> PlanOneAsync(
    SwitchLoadChange item,
    PlanScope scope,
    CancellationToken ct
  )
  {
    var (
      request,
      loads,
      sourceAssignments,
      operation,
      touched,
      changes,
      trips,
      now,
      actor
    ) = scope;
    var load = loads[item.DispatchId];
    var split = SwitchPlanningRules.Split(load, item, request.ConfirmCompleted);
    if (split is null)
      return Fail("The source visits changed. Review this transfer.", 409);
    var outgoing = item.OutgoingLegId is { } existingId
      ? await db
        .ExecutionLegs.Include(x => x.Loads)
        .Include(x => x.Trip)
        .SingleOrDefaultAsync(x => x.Id == existingId, ct)
      : null;
    if (item.OutgoingLegId.HasValue)
    {
      if (
        outgoing is null
        || outgoing.Status is not ("active" or "planned")
        || outgoing.SourceReviewReason is not null
        || outgoing.EndSwitchId.HasValue
        || outgoing.Loads.Count != 1
        || outgoing.Loads[0].DispatchId != load.Id
        || ExecutionCommandSupport.Assignment(outgoing) != item.Outgoing
        || item.ExpectedOutgoingRevision != outgoing.Revision
        || !await db.LockExecutionLegAsync(outgoing.Id, outgoing.Revision, ct)
      )
        return Fail("The outgoing assignment changed.", 409);
      var retained = ExecutionStopRows.Read(outgoing);
      var boundary = retained.FindIndex(x => x.Id == split.Value.Before[^1].Id);
      if (boundary < 0 || retained.Skip(boundary + 1).Any(x => x.IsCompleted))
        return Fail("Existing execution history needs reconciliation.", 409);
      split = (retained.Take(boundary + 1).ToList(), split.Value.After);
    }
    else
    {
      var status = await SwitchSourceAssignment.StatusAsync(
        db,
        load,
        item,
        split.Value.Before,
        request.ConfirmCompleted,
        ct
      );
      if (status is null)
        return Fail("Review the outgoing source assignment and boundary.", 409);
      var trip = await TripAsync(
        item.OutgoingTripId,
        item.Outgoing.TruckId,
        scope,
        ct
      );
      if (trip is null)
        return Fail("The outgoing trip is no longer available.", 409);
      outgoing = Leg(scope, load, trip, item.Outgoing);
      outgoing.Status = status;
      if (status == "active")
        trip.Status = status;
      db.ExecutionLegs.Add(outgoing);
    }
    trips.TryAdd(outgoing.TruckId, outgoing.Trip);
    var incomingTrip = await TripAsync(
      item.IncomingTripId,
      item.Incoming.TruckId,
      scope,
      ct
    );
    if (incomingTrip is null)
      return Fail("The incoming trip is no longer available.", 409);
    var incoming = Leg(scope, load, incomingTrip, item.Incoming);
    var restoredStops = item.OutgoingLegId.HasValue
      ? ExecutionStopRows.Read(outgoing)
      : StopOperation.Resolve(load.Stops, load.PlanningFromStopId);
    var restore = new SwitchOutgoingRestore(
      ExecutionSnapshots.Write(restoredStops),
      outgoing.SourceSignature,
      outgoing.Status,
      outgoing.StartedAt,
      outgoing.CompletedAt,
      outgoing.EndSwitchId,
      restoredStops[0].Id,
      restoredStops[^1].Id,
      outgoing.RouteChoiceRevision,
      item.OutgoingLegId.HasValue ? outgoing.Revision + 1 : 1
    );
    var release = Visit(
      scope,
      outgoing.TripId,
      item.ReleaseVisitId,
      item.TransferKind == "drop_hook" ? "Drop" : "Release",
      item.PlannedReleaseAt ?? request.PlannedAt
    );
    var receive = Visit(
      scope,
      incoming.TripId,
      item.ReceiveVisitId,
      item.TransferKind == "drop_hook" ? "Hook" : "Receive",
      item.PlannedReceiveAt ?? request.PlannedAt
    );
    var before = split.Value.Before;
    var after = split.Value.After;
    var cargo = before[^1].StateAfter;
    before.Add(
      ExecutionSnapshots.Boundary(release, load.Id, before.Count, cargo)
    );
    after.Insert(0, ExecutionSnapshots.Boundary(receive, load.Id, 0, cargo));
    for (var i = 0; i < after.Count; i++)
      after[i].Sequence = i;
    outgoing.SourceSignature = ExecutionSnapshots.Fingerprint(load);
    outgoing.EndSwitchId = operation.Id;
    incoming.StartSwitchId = operation.Id;
    var link = outgoing.Loads.SingleOrDefault();
    if (link is null)
    {
      link = Link(load.Id, outgoing.Id, 0, before);
      outgoing.Loads.Add(link);
    }
    else
      link.EndVisitId = release.Id;
    incoming.Loads.Add(Link(load.Id, incoming.Id, link.Sequence + 1, after));
    db.ExecutionLegs.Add(incoming);
    changes.Add(
      new(outgoing, before)
      {
        SourceReferences = new Dictionary<Guid, Guid?>
        {
          [release.Id] = release.SourceDispatchStopId,
        },
      }
    );
    changes.Add(
      new(incoming, after)
      {
        SourceReferences = new Dictionary<Guid, Guid?>
        {
          [receive.Id] = receive.SourceDispatchStopId,
        },
      }
    );
    var participant = new SwitchParticipant
    {
      Id = Guid.NewGuid(),
      SwitchId = operation.Id,
      DispatchId = load.Id,
      OutgoingLegId = outgoing.Id,
      IncomingLegId = incoming.Id,
      ReleaseVisitId = release.Id,
      ReceiveVisitId = receive.Id,
      TransferKind = item.TransferKind,
      PlannedReleaseAt = release.PlannedAt,
      PlannedReceiveAt = receive.PlannedAt,
      Revision = 1,
      OutgoingRestoreJson = restore.Serialize(),
    };
    operation.Participants.Add(participant);
    if (request.ConfirmCompleted)
      CompletedSwitchBootstrap.Apply(
        db,
        operation,
        participant,
        outgoing,
        incoming,
        actor
      );
    touched.Add(outgoing);
    touched.Add(incoming);
    return null;
  }

  // A transfer's trips: the one the request names, or one new trip per
  // truck shared by every load moving onto it.
  private async Task<Trip?> TripAsync(
    Guid? requestedId,
    Guid truckId,
    PlanScope scope,
    CancellationToken ct
  )
  {
    if (requestedId is { } tripId)
      return await db.Trips.SingleOrDefaultAsync(
        x => x.Id == tripId && (x.Status == "active" || x.Status == "planned"),
        ct
      );
    if (scope.Trips.TryGetValue(truckId, out var known))
      return known;
    var value = new Trip
    {
      Id = Guid.NewGuid(),
      Name = scope.Request.SiteName.Trim(),
      Revision = 1,
      RecordedAt = scope.Now,
      RecordedBy = scope.Actor,
    };
    db.Trips.Add(value);
    scope.Trips[truckId] = value;
    return value;
  }

  private static ExecutionLeg Leg(
    PlanScope scope,
    DispatchEntity load,
    Trip trip,
    ExecutionAssignment assignment
  ) =>
    new()
    {
      Id = Guid.NewGuid(),
      TripId = trip.Id,
      Trip = trip,
      TruckId = assignment.TruckId,
      DriverId = assignment.DriverId,
      CoDriverId = assignment.CoDriverId,
      TrailerId = assignment.TrailerId,
      Revision = 1,
      SourceSignature = ExecutionSnapshots.Fingerprint(load),
      SourceAssignmentSignature = scope.SourceAssignments.GetValueOrDefault(
        load.Id,
        ""
      ),
      RecordedAt = scope.Now,
      RecordedBy = scope.Actor,
    };

  private static ExecutionTransferVisit Visit(
    PlanScope scope,
    Guid tripId,
    Guid? sourceId,
    string action,
    DateTimeOffset? planned
  ) =>
    new()
    {
      Id = Guid.NewGuid(),
      TripId = tripId,
      SourceDispatchStopId = sourceId,
      Operation = action,
      SiteName = scope.Operation.SiteName,
      Latitude = scope.Operation.Latitude,
      Longitude = scope.Operation.Longitude,
      PlannedAt = planned?.UtcDateTime,
      Revision = 1,
    };
}
