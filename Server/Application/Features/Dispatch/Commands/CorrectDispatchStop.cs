using System.Data;
using Application.Caching;
using Application.Concurrency;
using Application.Features.Dispatch.Models;
using Application.Features.Dispatch.Services;
using Application.Features.Execution.Services;
using Application.Features.Routing.Background;
using Application.Models;
using Domain.Entities.Dispatch;
using Domain.Models.Execution;

namespace Application.Features.Dispatch.Commands;

public sealed record CorrectDispatchStopCommand(
  Guid DispatchId,
  Guid StopId,
  StopCorrectionRequest Request
) : IRequest<RequestResponse<DispatchWorkspaceResponse>>;

public sealed class CorrectDispatchStopHandler(
  IAppDbContext db,
  ICurrentUser caller,
  IUserRoleService roles,
  TimeProvider clock,
  ReadCache reads,
  RoutePreparationQueue preparation
)
  : IRequestHandler<
    CorrectDispatchStopCommand,
    RequestResponse<DispatchWorkspaceResponse>
  >
{
  public async Task<RequestResponse<DispatchWorkspaceResponse>> Handle(
    CorrectDispatchStopCommand command,
    CancellationToken ct
  )
  {
    var actor = await ExecutionCommandSupport.ActorAsync(db, caller, roles, ct);
    if (actor is null)
      return Fail("Access denied.", 403);
    var request = command.Request;
    if (
      request is null
      || command.DispatchId == Guid.Empty
      || command.StopId == Guid.Empty
      || request.IdempotencyKey == Guid.Empty
      || request.ExpectedRevision < 0
      || request.ExpectedRevision == long.MaxValue
      || request.SourceFingerprint?.Length != 64
      || request.Reason?.Length > 300
      || request.Completion is not ("keep" or "completed" or "pending")
      || request.Completion == "keep" && !request.ChangeAssignment
      || request.Completion != "completed" && request.CompletedAt.HasValue
    )
      return Fail(
        "Choose a valid status or assignment change. Optional notes must not exceed 300 characters.",
        400
      );
    var now = clock.GetUtcNow().UtcDateTime;
    if (
      request.CompletedAt is { } time
      && (time > clock.GetUtcNow() || time.Year < 2000)
    )
      return Fail("Completion must be an actual time, not in the future.", 400);
    var hash = DispatchWorkspaceData.Hash(command);
    await ProcessGates.Dispatch.WaitAsync(ct);
    try
    {
      await using var transaction = await db.Database.BeginTransactionAsync(
        IsolationLevel.Serializable,
        ct
      );
      var receipt = await db
        .DispatchWorkspaceRevisions.AsNoTracking()
        .SingleOrDefaultAsync(
          x => x.IdempotencyKey == request.IdempotencyKey,
          ct
        );
      if (receipt is not null)
        return
          receipt.RequestHash == hash
          && receipt.RecordedBy == actor.Value
          && receipt.DispatchId == command.DispatchId
          ? RequestResponse<DispatchWorkspaceResponse>.Ok(
            DispatchWorkspaceData.Read<DispatchWorkspaceResponse>(
              receipt.SnapshotJson
            )
          )
          : Fail("The retry identity belongs to another correction.");
      var state = await DispatchWorkspaceReader.ReadAsync(
        db,
        command.DispatchId,
        true,
        ct
      );
      if (state is null)
        return Fail("Load not found.", 404);
      if (
        state.Response.Revision != request.ExpectedRevision
        || state.Response.SourceFingerprint != request.SourceFingerprint
      )
        return Fail(
          "This load changed. Reload before applying the correction."
        );
      var row = state.Response.Stops.SingleOrDefault(x =>
        x.Id == command.StopId
      );
      if (row is null)
        return Fail("Stop not found.", 404);
      if (!row.CanCorrect)
        return Fail(
          "Use the transfer workflow for a handoff; shared assignments need a coordinated correction."
        );
      var stop = state.EffectiveStops[command.StopId];
      if (request.CompletedAt?.UtcDateTime < stop.ArrivedAt)
        return Fail(
          "Completion cannot be earlier than the recorded arrival.",
          400
        );
      var leg = state.Legs.SingleOrDefault(x => x.Id == row.ExecutionLegId);
      if (
        request.Completion == "completed"
        && leg is { Status: "planned", StartSwitchId: not null }
      )
        return Fail("Confirm receipt before recording this assignment's work.");
      var scope = DispatchCorrectionScope.Resolve(
        state,
        command.StopId,
        request
      );
      if (scope.Error is not null)
        return Fail(scope.Error, 400);
      var transferCorrection = await DispatchTransferCorrection.PrepareAsync(
        db,
        state,
        command.StopId,
        request,
        scope.Targets,
        ct
      );
      if (transferCorrection.Error is not null)
        return Fail(transferCorrection.Error, 400);
      var selectedRequest = scope
        .Targets.Single(x => x.StopId == command.StopId)
        .Request;
      if (
        request.Completion == "pending"
        && leg is { Status: "completed", EndSwitchId: null }
      )
      {
        var truckId = selectedRequest.ChangeAssignment
          ? selectedRequest.TruckId
          : leg.TruckId;
        var driverId = selectedRequest.ChangeAssignment
          ? selectedRequest.DriverId
          : leg.DriverId;
        var trailerId = selectedRequest.ChangeAssignment
          ? selectedRequest.TrailerId
          : leg.TrailerId;
        if (
          await db.ExecutionLegs.AnyAsync(
            x =>
              x.Id != leg.Id
              && x.Status == "active"
              && (
                x.TruckId == truckId
                || trailerId != null && x.TrailerId == trailerId
                || driverId != null
                  && (x.DriverId == driverId || x.CoDriverId == driverId)
              ),
            ct
          )
        )
          return Fail(
            "Reopening this leg conflicts with another active assignment. Correct the assignment before reopening."
          );
      }
      foreach (var target in scope.Targets)
      {
        var problem = await DispatchStopCorrections.ValidateAssignmentAsync(
          db,
          target.Request,
          target.Leg,
          ct,
          transferCorrection.Plan?.TrailerId.HasValue == true
        );
        if (problem is not null)
          return Fail(problem, 400);
        if (
          target.Leg is { } affectedLeg
          && !await db.LockExecutionLegAsync(
            affectedLeg.Id,
            affectedLeg.Revision,
            ct
          )
        )
          return Fail("The assignment changed. Reload before correcting it.");
      }
      var before = DispatchWorkspaceData.Write(state.Response);
      var oldTrucks = state
        .Load.Stops.Select(x => x.TruckId)
        .Concat(state.Legs.Select(x => (Guid?)x.TruckId))
        .Append(state.Load.TruckId)
        .Where(x => x.HasValue)
        .Select(x => x!.Value)
        .ToHashSet();
      var workspace = state.Workspace;
      if (workspace is null)
      {
        workspace = new DispatchWorkspace
        {
          Id = state.Load.Id,
          MetadataJson = DispatchWorkspaceData.Write(state.Response.Metadata),
        };
        db.DispatchWorkspaces.Add(workspace);
      }
      if (!workspace.OwnsStops)
        workspace.SourceStopsJson = ExecutionSnapshots.Write(state.Load.Stops);
      workspace.OwnsStops = true;
      var actorName = await db
        .Users.Where(x => x.Id == actor.Value)
        .Select(x => x.Name)
        .SingleAsync(ct);
      var sourceLink = request.ChangeAssignment
        ? await db.DispatchSourceLinks.SingleOrDefaultAsync(
          x => x.DispatchId == state.Load.Id,
          ct
        )
        : null;
      var corrections = new List<ExecutionChange>();
      foreach (
        var target in scope.Targets.Where(x =>
          x.Request.ChangeAssignment || x.Request.Completion != "keep"
        )
      )
      {
        var correctedStops = await DispatchStopCorrections.ApplyAsync(
          db,
          state,
          state.EffectiveStops[target.StopId],
          target.Leg,
          target.Request,
          actor.Value,
          actorName,
          now,
          ct,
          target.DriverStops,
          target.CompletionStops
        );
        if (target.Request.ChangeAssignment && target.Leg is { } assigned)
          assigned.SourceAssignmentSignature =
            sourceLink?.AssignmentSignature ?? "";
        if (target.Leg is { } corrected)
          corrections.Add(new(corrected, correctedStops));
      }
      if (transferCorrection.Plan is { } transferPlan)
        DispatchTransferCorrection.Apply(transferPlan);
      string? initialReview = null;
      if (state.Legs.Count == 0 && request.ChangeAssignment)
      {
        var accepted = await InitialExecutionAssignment.AcceptAsync(
          db,
          state.Load,
          StopOperation.Resolve(
            state.Load.Stops,
            state.Load.PlanningFromStopId
          ),
          actor.Value,
          request.IdempotencyKey,
          now,
          ct
        );
        if (accepted.Leg is { } initial)
        {
          state.Legs.Add(initial);
          if (sourceLink is not null)
            sourceLink.ExecutionReviewReason = null;
        }
        else
          initialReview = accepted.ReviewReason;
      }
      await ExecutionAcceptance.ApplyAsync(
        db,
        corrections,
        "stop-corrected",
        actor.Value,
        request.IdempotencyKey,
        now,
        ct
      );
      workspace.Revision++;
      workspace.RecordedAt = now;
      workspace.RecordedBy = actor.Value;
      var history = new DispatchWorkspaceRevision
      {
        Id = Guid.NewGuid(),
        DispatchId = state.Load.Id,
        Revision = workspace.Revision,
        IdempotencyKey = request.IdempotencyKey,
        RequestHash = hash,
        BeforeJson = before,
        RecordedAt = now,
        RecordedBy = actor.Value,
        ActorName = actorName,
        Summary =
          $"Corrected stop {row.Sequence}: "
          + (request.Completion == "keep" ? "assignment" : request.Completion)
          + (
            request.ChangeAssignment
              ? $"; assignment scope {request.AssignmentScope}"
              : ""
          )
          + (
            string.IsNullOrWhiteSpace(request.Reason)
              ? "."
              : $". {request.Reason.Trim()}"
          ),
      };
      db.DispatchWorkspaceRevisions.Add(history);
      await db.SaveChangesAsync(ct);
      var saved = await DispatchWorkspaceReader.ReadAsync(
        db,
        state.Load.Id,
        true,
        ct
      );
      if (initialReview is not null)
        saved!.Response.SourceReviewReason = string.Join(
          " ",
          new[] { saved.Response.SourceReviewReason, initialReview }.Where(x =>
            !string.IsNullOrWhiteSpace(x)
          )
        );
      history.SnapshotJson = DispatchWorkspaceData.Write(saved!.Response);
      await db.SaveChangesAsync(ct);
      await transaction.CommitAsync(ct);
      foreach (
        var key in new[] { "dispatch", "board", "execution", "route-previews" }
      )
        reads.Invalidate(key);
      reads.Invalidate($"route:{state.Load.Id}");
      preparation.MarkDirty(state.Load.Id);
      foreach (var affected in state.Legs)
        reads.Invalidate($"route:{state.Load.Id}:leg:{affected.Id}");
      if (request.TruckId is { } truck)
        oldTrucks.Add(truck);
      foreach (var id in oldTrucks)
        preparation.MarkTruckDirty(id);
      return RequestResponse<DispatchWorkspaceResponse>.Ok(saved.Response);
    }
    catch (Exception ex) when (db.IsWriteConflict(ex))
    {
      return Fail(
        "The load changed concurrently. Reload before correcting it."
      );
    }
    finally
    {
      ProcessGates.Dispatch.Release();
    }
  }

  private static RequestResponse<DispatchWorkspaceResponse> Fail(
    string message,
    int status = 409
  ) => RequestResponse<DispatchWorkspaceResponse>.Fail(message, status);
}
