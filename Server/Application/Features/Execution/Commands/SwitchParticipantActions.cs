using System.Data;
using System.Text.Json;
using Application.Caching;
using Application.Concurrency;
using Application.Features.Execution.Models;
using Application.Features.Execution.Services;
using Application.Features.Routing.Background;
using Application.Models;
using Domain.Entities.Execution;
using Microsoft.Extensions.Logging;

namespace Application.Features.Execution.Commands;

public sealed record ReleaseSwitchParticipantCommand(
  SwitchParticipantAction Action
) : IRequest<RequestResponse<SwitchResult>>;

public sealed record ReceiveSwitchParticipantCommand(
  SwitchParticipantAction Action
) : IRequest<RequestResponse<SwitchResult>>;

public sealed class ReleaseSwitchParticipantHandler(
  IAppDbContext db,
  ICurrentUser caller,
  IUserRoleService roles,
  TimeProvider clock,
  ReadCache reads,
  RoutePreparationQueue preparation,
  ILogger<ReleaseSwitchParticipantHandler> logger
)
  : IRequestHandler<
    ReleaseSwitchParticipantCommand,
    RequestResponse<SwitchResult>
  >
{
  public Task<RequestResponse<SwitchResult>> Handle(
    ReleaseSwitchParticipantCommand request,
    CancellationToken ct
  ) =>
    new SwitchParticipantMutation(
      db,
      caller,
      roles,
      clock,
      reads,
      preparation,
      logger
    ).ExecuteAsync(request.Action, false, ct);
}

public sealed class ReceiveSwitchParticipantHandler(
  IAppDbContext db,
  ICurrentUser caller,
  IUserRoleService roles,
  TimeProvider clock,
  ReadCache reads,
  RoutePreparationQueue preparation,
  ILogger<ReceiveSwitchParticipantHandler> logger
)
  : IRequestHandler<
    ReceiveSwitchParticipantCommand,
    RequestResponse<SwitchResult>
  >
{
  public Task<RequestResponse<SwitchResult>> Handle(
    ReceiveSwitchParticipantCommand request,
    CancellationToken ct
  ) =>
    new SwitchParticipantMutation(
      db,
      caller,
      roles,
      clock,
      reads,
      preparation,
      logger
    ).ExecuteAsync(request.Action, true, ct);
}

internal sealed partial class SwitchParticipantMutation(
  IAppDbContext db,
  ICurrentUser caller,
  IUserRoleService roles,
  TimeProvider clock,
  ReadCache reads,
  RoutePreparationQueue preparation,
  ILogger logger
)
{
  public async Task<RequestResponse<SwitchResult>> ExecuteAsync(
    SwitchParticipantAction request,
    bool receive,
    CancellationToken ct
  )
  {
    var actor = await ExecutionCommandSupport.ActorAsync(db, caller, roles, ct);
    if (actor is null)
      return Fail(
        "You cannot record a transfer.",
        caller.IsAuthenticated ? 403 : 401
      );
    var now = clock.GetUtcNow();
    if (
      request is null
      || request.IdempotencyKey == Guid.Empty
      || request.SwitchId == Guid.Empty
      || request.ParticipantId == Guid.Empty
      || request.OperationRevision < 1
      || request.ParticipantRevision < 1
      || request.LegRevision < 1
      || request.OccurredAt > now
      || request.OccurredAt is { Year: < 2000 }
    )
      return Fail(
        "Provide valid current revisions and an optional actual time."
      );
    var action = receive ? "receive" : "release";
    var hash = ExecutionCommandSupport.Hash(
      new { Action = action, Request = request }
    );
    await ProcessGates.Dispatch.WaitAsync(ct);
    try
    {
      await using var transaction = await db.Database.BeginTransactionAsync(
        IsolationLevel.Serializable,
        ct
      );
      var receipt = await db
        .ExecutionActionReceipts.AsNoTracking()
        .SingleOrDefaultAsync(
          x => x.IdempotencyKey == request.IdempotencyKey,
          ct
        );
      if (receipt is not null)
        return receipt.RequestHash == hash
          ? RequestResponse<SwitchResult>.Ok(
            JsonSerializer.Deserialize<SwitchResult>(receipt.ResultJson)!
          )
          : Fail("The retry key belongs to a different event.", 409);
      var operation = await db
        .DispatchSwitchOperations.Include(x => x.Participants)
        .SingleOrDefaultAsync(x => x.Id == request.SwitchId, ct);
      var participant = operation?.Participants.SingleOrDefault(x =>
        x.Id == request.ParticipantId
      );
      if (operation is null || participant is null)
        return Fail("Transfer participant not found.", 404);
      if (
        operation.Status is not ("planned" or "in_progress")
        || participant.IsCancelled
        || operation.Revision != request.OperationRevision
        || participant.Revision != request.ParticipantRevision
      )
        return Fail(
          "The transfer changed. Reload before recording this event.",
          409
        );
      var legId = receive
        ? participant.IncomingLegId
        : participant.OutgoingLegId;
      if (!await db.LockExecutionLegAsync(legId, request.LegRevision, ct))
        return Fail("The resource assignment changed.", 409);
      var leg = await db
        .ExecutionLegs.Include(x => x.Trip)
        .Include(x => x.Loads)
        .SingleAsync(x => x.Id == legId, ct);
      if ((receive ? leg.StartSwitchId : leg.EndSwitchId) != operation.Id)
        return Fail(
          "The execution boundary no longer belongs to this transfer.",
          409
        );
      var visitId = receive
        ? participant.ReceiveVisitId
        : participant.ReleaseVisitId;
      if (!leg.Stops.Any(x => x.Id == visitId))
        return Fail(
          "The execution boundary is no longer in this assignment.",
          409
        );
      var at = request.OccurredAt?.UtcDateTime;
      var error = receive
        ? await ReceiveAsync(participant, leg, at, actor.Value, ct)
        : await ReleaseAsync(participant, leg, at, actor.Value, ct);
      if (error is not null)
        return Fail(error, 409);
      participant.Revision++;
      operation.Revision++;
      operation.Status = operation.Participants.All(x => x.ReceivedBy.HasValue)
        ? "completed"
        : "in_progress";
      if (operation.Status == "completed")
      {
        operation.CompletedAt = operation.Participants.All(x =>
          x.ReceivedAt.HasValue
        )
          ? operation.Participants.Max(x => x.ReceivedAt)
          : null;
        operation.CompletedBy = actor.Value;
      }
      var result = ExecutionCommandSupport.Result(operation);
      db.ExecutionActionReceipts.Add(
        new()
        {
          Id = Guid.NewGuid(),
          IdempotencyKey = request.IdempotencyKey,
          SwitchId = operation.Id,
          ParticipantId = participant.Id,
          Action = action,
          RequestHash = hash,
          ResultJson = JsonSerializer.Serialize(result),
          RecordedAt = now.UtcDateTime,
          RecordedBy = actor.Value,
        }
      );
      await ExecutionAcceptance.ApplyAsync(
        db,
        [new(leg, ExecutionStopRows.Read(leg))],
        $"transfer-{action}",
        actor.Value,
        request.IdempotencyKey,
        now.UtcDateTime,
        ct
      );
      await db.SaveChangesAsync(ct);
      await transaction.CommitAsync(ct);
      ExecutionCommandSupport.Invalidate(operation, [leg], reads, preparation);
      logger.LogInformation(
        "Transfer {SwitchId} participant {ParticipantId} "
          + "recorded {Action} by {ActorId}",
        operation.Id,
        participant.Id,
        action,
        actor.Value
      );
      return RequestResponse<SwitchResult>.Ok(result);
    }
    catch (Exception ex) when (db.IsWriteConflict(ex))
    {
      return Fail(
        "The transfer changed concurrently. Reload before retrying.",
        409
      );
    }
    finally
    {
      ProcessGates.Dispatch.Release();
    }
  }

  private static RequestResponse<SwitchResult> Fail(
    string message,
    int status = 400
  ) => RequestResponse<SwitchResult>.Fail(message, status);
}
