using System.Data;
using System.Text.Json;
using Application.Caching;
using Application.Concurrency;
using Application.Features.Execution.Models;
using Application.Features.Execution.Services;
using Application.Features.Routing.Background;
using Application.Models;

namespace Application.Features.Execution.Commands;

public sealed record AcceptExecutionSourceChangesCommand(
  Guid DispatchId,
  AcceptExecutionSourceChangesRequest Request
) : IRequest<RequestResponse<ExecutionSourceApplyResult>>;

public sealed class AcceptExecutionSourceChangesHandler(
  IAppDbContext db,
  ICurrentUser caller,
  IUserRoleService roles,
  TimeProvider clock,
  ReadCache reads,
  RoutePreparationQueue preparation
)
  : IRequestHandler<
    AcceptExecutionSourceChangesCommand,
    RequestResponse<ExecutionSourceApplyResult>
  >
{
  public async Task<RequestResponse<ExecutionSourceApplyResult>> Handle(
    AcceptExecutionSourceChangesCommand command,
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
      || request.SourceSignature?.Length != 64
    )
      return Fail(
        "Provide the reviewed assignment, revision and retry key.",
        400
      );
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
          : Fail("The retry key belongs to a different source update.");
      var now = clock.GetUtcNow().UtcDateTime;
      var state = await ExecutionSourceReviewReader.ReadAsync(
        db,
        command.DispatchId,
        request.ExecutionLegId,
        now,
        ct
      );
      if (state is null)
        return Fail("Assignment not found.", 404);
      if (
        state.Leg.Revision != request.ExpectedRevision
        || state.Review.SourceSignature != request.SourceSignature
      )
        return Fail("The source or assignment changed. Preview it again.");
      if (!state.Review.CanApply)
        return Fail(string.Join(" ", state.Review.Problems));
      var leg = state.Leg;
      leg.SourceObservedSignature = request.SourceSignature;
      await ExecutionAcceptance.ApplyAsync(
        db,
        [
          new(leg, state.Stops)
          {
            SourceSignature = request.SourceSignature,
            ReviewReason = null,
          },
        ],
        "source-accepted",
        actor.Value,
        request.IdempotencyKey,
        now,
        ct
      );
      var result = new ExecutionSourceApplyResult(
        command.DispatchId,
        leg.Id,
        leg.Revision
      )
      {
        Changes = state.Review.Changes,
      };
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
      preparation.MarkTruckDirty(leg.TruckId);
      reads.Invalidate($"route:{command.DispatchId}");
      reads.Invalidate($"route:{command.DispatchId}:leg:{leg.Id}");
      foreach (
        var key in new[] { "dispatch", "board", "execution", "route-previews" }
      )
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
