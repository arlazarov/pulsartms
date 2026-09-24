using System.Data;
using System.Text.Json;
using Application.Caching;
using Application.Concurrency;
using Application.Features.Execution.Models;
using Application.Features.Execution.Services;
using Application.Features.Routing.Background;
using Application.Models;
using Domain.Rules;

namespace Application.Features.Execution.Commands;

public sealed record CloseCancelledExecutionRequest(
  Guid ExecutionLegId,
  long ExpectedRevision,
  Guid IdempotencyKey
);

public sealed record CloseCancelledExecutionCommand(
  Guid DispatchId,
  CloseCancelledExecutionRequest Request
) : IRequest<RequestResponse<ExecutionSourceApplyResult>>;

// A dispatcher closing work held after the source cancelled its load. The
// work becomes cancelled; its stops, movements and revisions stay as they
// are. It is refused unless the work is still held, the load is still
// cancelled at the source and nothing changed since the dispatcher looked;
// a repeated request with the same key returns the first answer.
public sealed class CloseCancelledExecutionHandler(
  IAppDbContext db,
  ICurrentUser caller,
  IUserRoleService roles,
  TimeProvider clock,
  ReadCache reads,
  RoutePreparationQueue preparation
)
  : IRequestHandler<
    CloseCancelledExecutionCommand,
    RequestResponse<ExecutionSourceApplyResult>
  >
{
  public async Task<RequestResponse<ExecutionSourceApplyResult>> Handle(
    CloseCancelledExecutionCommand command,
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
      || request.ExecutionLegId == Guid.Empty
      || request.ExpectedRevision < 1
      || request.IdempotencyKey == Guid.Empty
    )
      return Fail("Provide the held assignment, revision and retry key.", 400);
    var hash = ExecutionCommandSupport.Hash(command);
    await ProcessGates.Dispatch.WaitAsync(ct);
    try
    {
      await using var transaction = await db.Database.BeginTransactionAsync(
        IsolationLevel.Serializable,
        ct
      );
      var receipt = await db
        .ExecutionSourceReceipts.AsNoTracking()
        .SingleOrDefaultAsync(
          x => x.IdempotencyKey == request.IdempotencyKey,
          ct
        );
      if (receipt is not null)
        return receipt.RequestHash == hash
          ? RequestResponse<ExecutionSourceApplyResult>.Ok(
            JsonSerializer.Deserialize<ExecutionSourceApplyResult>(
              receipt.ResultJson
            )!
          )
          : Fail("The retry key belongs to a different request.");
      var link = await db
        .LoadExecutionLegs.Include(x => x.ExecutionLeg)
        .ThenInclude(x => x.Loads)
        .SingleOrDefaultAsync(
          x =>
            x.DispatchId == command.DispatchId
            && x.ExecutionLegId == request.ExecutionLegId,
          ct
        );
      if (link is null)
        return Fail("Assignment not found.", 404);
      var leg = link.ExecutionLeg;
      if (leg.Status != SourceCancellation.Held)
        return Fail(
          "Only work held after the source cancelled its load is closed here."
        );
      var status = await db
        .Dispatches.Where(x => x.Id == command.DispatchId)
        .Select(x => x.Status)
        .SingleAsync(ct);
      if (!SourceWords.IsCancelled(status))
        return Fail(
          "The source no longer shows this load as cancelled. Review it before "
            + "closing."
        );
      if (leg.Revision != request.ExpectedRevision)
        return Fail("The assignment changed. Reload it before closing.");
      var now = clock.GetUtcNow().UtcDateTime;
      await ExecutionAcceptance.ApplyAsync(
        db,
        [
          new(leg, ExecutionStopRows.Read(leg))
          {
            SourceSignature = leg.SourceSignature,
            ReviewReason = null,
            SupersedePlannedMileage = true,
            Status = "cancelled",
          },
        ],
        "source-cancellation-closed",
        actor.Value,
        request.IdempotencyKey,
        now,
        ct
      );
      var result = new ExecutionSourceApplyResult(
        command.DispatchId,
        leg.Id,
        leg.Revision
      );
      db.ExecutionSourceReceipts.Add(
        new()
        {
          Id = Guid.NewGuid(),
          IdempotencyKey = request.IdempotencyKey,
          ExecutionLegId = leg.Id,
          RequestHash = hash,
          ResultJson = JsonSerializer.Serialize(result),
          RecordedAt = now,
          RecordedBy = actor.Value,
        }
      );
      await db.SaveChangesAsync(ct);
      await transaction.CommitAsync(ct);
      preparation.MarkDirty(command.DispatchId);
      preparation.MarkTruckDirty(leg.TruckId, reads);
      reads.Invalidate($"route:{command.DispatchId}");
      reads.Invalidate($"route:{command.DispatchId}:leg:{leg.Id}");
      foreach (var key in ReadGroups.Work)
        reads.Invalidate(key);
      return RequestResponse<ExecutionSourceApplyResult>.Ok(result);
    }
    catch (Exception ex) when (db.IsWriteConflict(ex))
    {
      return Fail("Execution changed concurrently. Retry the same request.");
    }
    finally
    {
      ProcessGates.Dispatch.Release();
    }
  }

  private static RequestResponse<ExecutionSourceApplyResult> Fail(
    string message,
    int status = 409
  ) => RequestResponse<ExecutionSourceApplyResult>.Fail(message, status);
}
