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

public sealed record CancelSwitchCommand(
  Guid SwitchId,
  CancelSwitchRequest Request
) : IRequest<RequestResponse<SwitchResult>>;

public sealed class CancelSwitchHandler(
  IAppDbContext db,
  ICurrentUser caller,
  IUserRoleService roles,
  TimeProvider clock,
  ReadCache reads,
  RoutePreparationQueue preparation,
  ILogger<CancelSwitchHandler> logger
) : IRequestHandler<CancelSwitchCommand, RequestResponse<SwitchResult>>
{
  public async Task<RequestResponse<SwitchResult>> Handle(
    CancelSwitchCommand command,
    CancellationToken ct
  )
  {
    var actor = await ExecutionCommandSupport.ActorAsync(db, caller, roles, ct);
    if (actor is null)
      return Fail(
        "You cannot cancel a switch.",
        caller.IsAuthenticated ? 403 : 401
      );
    if (
      command.SwitchId == Guid.Empty
      || command.Request is null
      || command.Request.IdempotencyKey == Guid.Empty
      || command.Request.Revision < 1
    )
      return Fail("Provide the switch revision and a retry key.", 400);
    var hash = ExecutionCommandSupport.Hash(command);
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
          x => x.IdempotencyKey == command.Request.IdempotencyKey,
          ct
        );
      if (receipt is not null)
        return receipt.RequestHash == hash
          ? RequestResponse<SwitchResult>.Ok(
            JsonSerializer.Deserialize<SwitchResult>(receipt.ResultJson)!
          )
          : Fail("The retry key belongs to another action.");
      var operation = await db
        .DispatchSwitchOperations.Include(x => x.Participants)
        .SingleOrDefaultAsync(x => x.Id == command.SwitchId, ct);
      if (operation is null)
        return Fail("Switch not found.", 404);
      if (
        operation.Status != "planned"
        || operation.Revision != command.Request.Revision
        || operation.Participants.Count == 0
        || operation.Participants.Any(x =>
          x.IsCancelled || x.ReleasedBy.HasValue || x.ReceivedBy.HasValue
        )
      )
        return Fail(
          "Only an unchanged switch without actual events can be cancelled."
        );
      var participants = operation.Participants;
      var legIds = participants
        .SelectMany(x => new[] { x.OutgoingLegId, x.IncomingLegId })
        .Distinct()
        .ToArray();
      var legs = await db
        .ExecutionLegs.Include(x => x.Loads)
        .Where(x => legIds.Contains(x.Id))
        .ToDictionaryAsync(x => x.Id, ct);
      if (legs.Count != legIds.Length)
        return Fail("The execution history is incomplete.");
      foreach (var leg in legs.Values.OrderBy(x => x.Id))
        if (!await db.LockExecutionLegAsync(leg.Id, leg.Revision, ct))
          return Fail("The execution assignment changed.");
      var restores = participants.ToDictionary(
        x => x.Id,
        SwitchOutgoingRestore.Read
      );
      foreach (var participant in participants)
      {
        var restore = restores[participant.Id];
        var outgoing = legs[participant.OutgoingLegId];
        var incoming = legs[participant.IncomingLegId];
        if (
          restore is null
          || outgoing.EndSwitchId != operation.Id
          || outgoing.Revision != restore.PlannedRevision
          || outgoing.RouteChoiceRevision != restore.RouteChoiceRevision
          || outgoing.Status != restore.Status
          || incoming.StartSwitchId != operation.Id
          || incoming.EndSwitchId.HasValue
          || incoming.Status != "planned"
          || incoming.Revision != 1
          || incoming.RouteChoiceRevision != 0
          || outgoing.Loads.Count != 1
          || incoming.Loads.Count != 1
        )
          return Fail(
            "Dependent planning or execution changed. Review the switch first."
          );
      }
      if (
        await db.TrailerCustodyIntervals.AnyAsync(
          x => participants.Select(p => p.Id).Contains(x.ParticipantId),
          ct
        )
      )
        return Fail(
          "Recorded transfer or custody history cannot be cancelled."
        );
      var loadIds = participants.Select(x => x.DispatchId).ToArray();
      if (
        await db.DispatchStopCompletionEvents.AnyAsync(
          x =>
            loadIds.Contains(x.DispatchId)
            && x.RecordedAt >= operation.RecordedAt,
          ct
        )
        || await db.DispatchStops.AnyAsync(
          x =>
            loadIds.Contains(x.DispatchId)
            && (
              x.DepartedAt >= operation.RecordedAt
              || x.DeliveredAt >= operation.RecordedAt
              || x.PickedUpAt >= operation.RecordedAt
              || x.ArrivedAt >= operation.RecordedAt
            ),
          ct
        )
        || await db.Movements.AnyAsync(
          x =>
            x.ExecutionLegId.HasValue
            && legIds.Contains(x.ExecutionLegId.Value)
            && (
              x.RecordedAt >= operation.RecordedAt
                && (
                  x.Origin != "native-route"
                  || x.ManualOverride
                  || x.StartedAt != null
                  || x.EndedAt != null
                  || x.ActualEvidenceId != null
                  || x.ActualMiles != null
                )
              || x.StartedAt >= operation.RecordedAt
              || x.EndedAt >= operation.RecordedAt
              || x.ActualAt >= operation.RecordedAt
            ),
          ct
        )
        || await (
          from evidence in db.MovementDistanceEvidence
          join movement in db.Movements
            on evidence.MovementId equals movement.Id
          where
            movement.ExecutionLegId.HasValue
            && legIds.Contains(movement.ExecutionLegId.Value)
            && evidence.RecordedAt >= operation.RecordedAt
            && (
              evidence.Basis != "planned"
              || evidence.Source != "saved-native-road"
              || evidence.RecordedBy != Guid.Empty
              || movement.ManualOverride
            )
          select evidence.Id
        ).AnyAsync(ct)
        || await (
          from change in db.MovementAllocationEvents
          join movement in db.Movements on change.MovementId equals movement.Id
          where
            movement.ExecutionLegId.HasValue
            && legIds.Contains(movement.ExecutionLegId.Value)
            && change.RecordedAt >= operation.RecordedAt
            && (change.RecordedBy != Guid.Empty || change.ManualOverride)
          select change.Id
        ).AnyAsync(ct)
      )
        return Fail(
          "New work or mileage evidence must be reconciled before cancellation."
        );
      var changes = new List<ExecutionChange>();
      foreach (var participant in participants)
      {
        var restore = restores[participant.Id]!;
        var outgoing = legs[participant.OutgoingLegId];
        var incoming = legs[participant.IncomingLegId];
        var stops = ExecutionSnapshots.Read(restore.StopsJson);
        outgoing.SourceSignature = restore.SourceSignature;
        outgoing.EndSwitchId = restore.EndSwitchId;
        outgoing.Status = restore.Status;
        outgoing.StartedAt = restore.StartedAt;
        outgoing.CompletedAt = restore.CompletedAt;
        outgoing.Loads[0].StartVisitId = restore.StartVisitId;
        outgoing.Loads[0].EndVisitId = restore.EndVisitId;
        incoming.Status = "cancelled";
        changes.Add(new(outgoing, stops) { SupersedePlannedMileage = true });
        changes.Add(
          new(incoming, ExecutionStopRows.Read(incoming))
          {
            SupersedePlannedMileage = true,
          }
        );
        participant.IsCancelled = true;
        participant.Revision++;
      }
      operation.Status = "cancelled";
      operation.Revision++;
      operation.CompletedAt = clock.GetUtcNow().UtcDateTime;
      operation.CompletedBy = actor.Value;
      foreach (var participant in participants)
        db.LoadExecutionLegs.Remove(legs[participant.IncomingLegId].Loads[0]);
      await ExecutionAcceptance.ApplyAsync(
        db,
        changes,
        "transfer-cancelled",
        actor.Value,
        command.Request.IdempotencyKey,
        clock.GetUtcNow().UtcDateTime,
        ct
      );
      var result = ExecutionCommandSupport.Result(operation);
      db.ExecutionActionReceipts.Add(
        new()
        {
          Id = Guid.NewGuid(),
          IdempotencyKey = command.Request.IdempotencyKey,
          SwitchId = operation.Id,
          ParticipantId = participants[0].Id,
          Action = "cancel",
          RequestHash = hash,
          ResultJson = JsonSerializer.Serialize(result),
          RecordedAt = clock.GetUtcNow().UtcDateTime,
          RecordedBy = actor.Value,
        }
      );
      await db.SaveChangesAsync(ct);
      await transaction.CommitAsync(ct);
      ExecutionCommandSupport.Invalidate(
        operation,
        legs.Values,
        reads,
        preparation
      );
      logger.LogInformation(
        "Switch {SwitchId} cancelled by {ActorId}",
        operation.Id,
        actor.Value
      );
      return RequestResponse<SwitchResult>.Ok(result);
    }
    catch (Exception ex) when (db.IsWriteConflict(ex))
    {
      return Fail("The switch changed concurrently. Reload before retrying.");
    }
    finally
    {
      ProcessGates.Dispatch.Release();
    }
  }

  private static RequestResponse<SwitchResult> Fail(
    string message,
    int status = 409
  ) => RequestResponse<SwitchResult>.Fail(message, status);
}
