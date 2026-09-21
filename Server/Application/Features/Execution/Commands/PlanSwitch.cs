using System.Data;
using Application.Caching;
using Application.Concurrency;
using Application.Features.Execution.Models;
using Application.Features.Execution.Services;
using Application.Features.Routing.Background;
using Application.Models;
using Domain.Entities.Dispatch;
using Domain.Entities.Execution;
using Domain.Models.Execution;
using Microsoft.Extensions.Logging;
using DispatchEntity = Domain.Entities.Dispatch.Dispatch;

namespace Application.Features.Execution.Commands;

public sealed record PlanSwitchCommand(PlanSwitchRequest Request)
  : IRequest<RequestResponse<SwitchResult>>;

public sealed class PlanSwitchHandler(
  IAppDbContext db,
  ICurrentUser caller,
  IUserRoleService roles,
  TimeProvider clock,
  ReadCache reads,
  RoutePreparationQueue preparation,
  ILogger<PlanSwitchHandler> logger
) : IRequestHandler<PlanSwitchCommand, RequestResponse<SwitchResult>>
{
  public async Task<RequestResponse<SwitchResult>> Handle(
    PlanSwitchCommand command,
    CancellationToken ct
  )
  {
    var actor = await ExecutionCommandSupport.ActorAsync(db, caller, roles, ct);
    if (actor is null)
      return Fail(
        "You cannot plan a transfer.",
        caller.IsAuthenticated ? 403 : 401
      );
    var request = command.Request;
    if (!SwitchPlanningRules.Valid(request))
      return Fail(
        "Select explicit transfer boundaries and resource assignments."
      );
    var hash = ExecutionCommandSupport.Hash(request);
    await ProcessGates.Dispatch.WaitAsync(ct);
    try
    {
      await using var transaction = await db.Database.BeginTransactionAsync(
        IsolationLevel.Serializable,
        ct
      );
      var previous = await db
        .DispatchSwitchOperations.Include(x => x.Participants)
        .SingleOrDefaultAsync(
          x => x.IdempotencyKey == request.IdempotencyKey,
          ct
        );
      if (previous is not null)
        return previous.RequestHash == hash
          ? RequestResponse<SwitchResult>.Ok(
            ExecutionCommandSupport.Result(previous)
          )
          : Fail("The retry key belongs to a different transfer.", 409);

      var ids = request.Loads.Select(x => x.DispatchId).ToArray();
      var loads = await db
        .Dispatches.Include(x => x.Stops)
        .Where(x => ids.Contains(x.Id))
        .ToDictionaryAsync(x => x.Id, ct);
      if (loads.Count != ids.Length)
        return Fail("A selected load is no longer available.", 409);
      var sourceAssignments = await db
        .DispatchSourceLinks.AsNoTracking()
        .Where(x => ids.Contains(x.DispatchId))
        .ToDictionaryAsync(x => x.DispatchId, x => x.AssignmentSignature, ct);
      var assignments = request
        .Loads.SelectMany(x => new[] { x.Outgoing, x.Incoming })
        .ToArray();
      if (!await ExecutionResources.ActiveAsync(db, assignments, ct))
        return Fail("Choose active resources and distinct driver roles.");

      if (request.ConfirmCompleted)
      {
        foreach (var item in request.Loads)
        {
          if (
            await ExecutionResources.ConflictsAsync(db, item.Incoming, null, ct)
          )
            return Fail("Incoming resources are assigned elsewhere.", 409);
        }
      }

      var now = clock.GetUtcNow().UtcDateTime;
      var operation = new DispatchSwitchOperation
      {
        Id = Guid.NewGuid(),
        IdempotencyKey = request.IdempotencyKey,
        SiteName = request.SiteName.Trim(),
        Latitude = request.Latitude!.Value,
        Longitude = request.Longitude!.Value,
        PlannedAt = request.PlannedAt?.UtcDateTime,
        RecordedAt = now,
        RecordedBy = actor.Value,
        RequestHash = hash,
        Revision = 1,
      };
      var touched = new List<ExecutionLeg>();
      var changes = new List<ExecutionChange>();
      var trips = new Dictionary<Guid, Trip>();
      foreach (var item in request.Loads.OrderBy(x => x.OutgoingLegId))
      {
        var load = loads[item.DispatchId];
        var split = SwitchPlanningRules.Split(
          load,
          item,
          request.ConfirmCompleted
        );
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
            || !await db.LockExecutionLegAsync(
              outgoing.Id,
              outgoing.Revision,
              ct
            )
          )
            return Fail("The outgoing assignment changed.", 409);
          var retained = ExecutionStopRows.Read(outgoing);
          var boundary = retained.FindIndex(x =>
            x.Id == split.Value.Before[^1].Id
          );
          if (
            boundary < 0
            || retained.Skip(boundary + 1).Any(x => x.IsCompleted)
          )
            return Fail(
              "Existing execution history needs reconciliation.",
              409
            );
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
            return Fail(
              "Review the outgoing source assignment and boundary.",
              409
            );
          var trip = await TripAsync(
            item.OutgoingTripId,
            item.Outgoing.TruckId
          );
          if (trip is null)
            return Fail("The outgoing trip is no longer available.", 409);
          outgoing = Leg(load, trip, item.Outgoing);
          outgoing.Status = status;
          if (status == "active")
            trip.Status = status;
          db.ExecutionLegs.Add(outgoing);
        }
        trips.TryAdd(outgoing.TruckId, outgoing.Trip);
        var incomingTrip = await TripAsync(
          item.IncomingTripId,
          item.Incoming.TruckId
        );
        if (incomingTrip is null)
          return Fail("The incoming trip is no longer available.", 409);
        var incoming = Leg(load, incomingTrip, item.Incoming);
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
          outgoing.TripId,
          item.ReleaseVisitId,
          item.TransferKind == "drop_hook" ? "Drop" : "Release",
          item.PlannedReleaseAt ?? request.PlannedAt
        );
        var receive = Visit(
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
        after.Insert(
          0,
          ExecutionSnapshots.Boundary(receive, load.Id, 0, cargo)
        );
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
        incoming.Loads.Add(
          Link(load.Id, incoming.Id, link.Sequence + 1, after)
        );
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
            actor.Value
          );
        touched.Add(outgoing);
        touched.Add(incoming);
      }
      db.DispatchSwitchOperations.Add(operation);
      await ExecutionAcceptance.ApplyAsync(
        db,
        changes,
        "transfer-planned",
        actor.Value,
        request.IdempotencyKey,
        now,
        ct
      );
      await db.SaveChangesAsync(ct);
      await transaction.CommitAsync(ct);
      ExecutionCommandSupport.Invalidate(
        operation,
        touched,
        reads,
        preparation
      );
      logger.LogInformation(
        "Transfer {SwitchId} planned by {ActorId} for {ParticipantCount} loads",
        operation.Id,
        actor.Value,
        operation.Participants.Count
      );
      return RequestResponse<SwitchResult>.Ok(
        ExecutionCommandSupport.Result(operation)
      );

      async Task<Trip?> TripAsync(Guid? requestedId, Guid truckId)
      {
        if (requestedId is { } tripId)
          return await db.Trips.SingleOrDefaultAsync(
            x =>
              x.Id == tripId && (x.Status == "active" || x.Status == "planned"),
            ct
          );
        if (trips.TryGetValue(truckId, out var known))
          return known;
        var value = new Trip
        {
          Id = Guid.NewGuid(),
          Name = request.SiteName.Trim(),
          Revision = 1,
          RecordedAt = now,
          RecordedBy = actor.Value,
        };
        db.Trips.Add(value);
        trips[truckId] = value;
        return value;
      }

      ExecutionLeg Leg(
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
          SourceAssignmentSignature = sourceAssignments.GetValueOrDefault(
            load.Id,
            ""
          ),
          RecordedAt = now,
          RecordedBy = actor.Value,
        };

      ExecutionTransferVisit Visit(
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
          SiteName = operation.SiteName,
          Latitude = operation.Latitude,
          Longitude = operation.Longitude,
          PlannedAt = planned?.UtcDateTime,
          Revision = 1,
        };
    }
    catch (Exception ex) when (db.IsWriteConflict(ex))
    {
      return Fail(
        "Assignments changed concurrently. Reload before retrying.",
        409
      );
    }
    finally
    {
      ProcessGates.Dispatch.Release();
    }
  }

  private static LoadExecutionLeg Link(
    Guid load,
    Guid leg,
    int sequence,
    List<DispatchStop> stops
  ) =>
    new()
    {
      Id = Guid.NewGuid(),
      DispatchId = load,
      ExecutionLegId = leg,
      Sequence = sequence,
      StartVisitId = stops[0].Id,
      EndVisitId = stops[^1].Id,
    };

  private static RequestResponse<SwitchResult> Fail(
    string message,
    int status = 400
  ) => RequestResponse<SwitchResult>.Fail(message, status);
}
