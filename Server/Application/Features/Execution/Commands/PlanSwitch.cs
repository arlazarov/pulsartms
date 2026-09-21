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

public sealed partial class PlanSwitchHandler(
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
      var scope = new PlanScope(
        request,
        loads,
        sourceAssignments,
        operation,
        touched,
        changes,
        trips,
        now,
        actor.Value
      );
      foreach (var item in request.Loads.OrderBy(x => x.OutgoingLegId))
        if (await PlanOneAsync(item, scope, ct) is { } failure)
          return failure;
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
