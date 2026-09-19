using System.Data;
using Application.Caching;
using Application.Concurrency;
using Application.Features.Dispatch.Models;
using Application.Features.Dispatch.Services;
using Application.Features.Routing.Background;
using Application.Models;
using Domain.Entities.Dispatch;
using Microsoft.Extensions.Logging;

namespace Application.Features.Dispatch.Commands;

public sealed record StopOperationUpdate(
  string? Action,
  string? StateAfter,
  long Revision,
  string StopIdentity
);

public sealed record StopOperationState(
  string? Action,
  string? StateAfter,
  long Revision,
  DateTime? RecordedAt
);

public sealed record SetStopOperationCommand(
  Guid DispatchId,
  Guid StopId,
  StopOperationUpdate Update
) : IRequest<RequestResponse<StopOperationState>>;

public sealed class SetStopOperationValidator
  : AbstractValidator<SetStopOperationCommand>
{
  public SetStopOperationValidator()
  {
    RuleFor(x => x.DispatchId).NotEmpty();
    RuleFor(x => x.StopId).NotEmpty();
    RuleFor(x => x.Update).NotNull();
    When(
      x => x.Update is not null,
      () =>
      {
        RuleFor(x => x.Update.Revision).InclusiveBetween(0, long.MaxValue - 1);
        RuleFor(x => x.Update.StopIdentity).NotEmpty().Length(64);
        RuleFor(x => x.Update)
          .Must(x => StopOperation.Valid(x.Action, x.StateAfter))
          .WithMessage("Choose a compatible action and state after the stop.");
      }
    );
  }
}

public sealed class SetStopOperationHandler(
  IAppDbContext db,
  ICurrentUser caller,
  IUserRoleService roles,
  TimeProvider clock,
  ReadCache reads,
  RoutePreparationQueue preparation,
  ILogger<SetStopOperationHandler> logger
)
  : IRequestHandler<
    SetStopOperationCommand,
    RequestResponse<StopOperationState>
  >
{
  public async Task<RequestResponse<StopOperationState>> Handle(
    SetStopOperationCommand request,
    CancellationToken ct
  )
  {
    if (!caller.IsAuthenticated || string.IsNullOrEmpty(caller.IdentityUserId))
      return Fail("Unauthorized.", 401);
    var actor = await db
      .Users.AsNoTracking()
      .SingleOrDefaultAsync(
        x => x.IdentityUserId == caller.IdentityUserId && x.IsActive,
        ct
      );
    if (
      actor is null
      || await roles.GetAsync(caller.IdentityUserId, ct)
        is not ("Admin" or "Dispatch")
    )
      return Fail("You cannot change stop operations.", 403);
    var update = request.Update;
    if (
      update is null
      || !StopOperation.Valid(update.Action, update.StateAfter)
    )
      return Fail("Choose a compatible action and state.");
    await ProcessGates.Dispatch.WaitAsync(ct);
    try
    {
      await using var transaction = await db.Database.BeginTransactionAsync(
        IsolationLevel.Serializable,
        ct
      );
      var state = await DispatchWorkspaceReader.ReadAsync(
        db,
        request.DispatchId,
        true,
        ct
      );
      var load = state?.Load;
      var stop = state?.EffectiveStops.GetValueOrDefault(request.StopId);
      if (state is null || load is null || stop is null)
        return Fail("Stop not found.", 404);
      var row = state.Response.Stops.Single(x => x.Id == stop.Id);
      var leg = state.Legs.SingleOrDefault(x => x.Id == row.ExecutionLegId);
      if (leg is not null && (!row.CanCorrect || row.Transfer is not null))
        return Fail("Use the transfer workflow for this execution visit.", 409);
      if (leg is not null && update.StateAfter == "No truck")
        return Fail("Driver travel needs a separate execution boundary.", 409);
      if (
        leg is { Status: "completed" or "cancelled" }
        || load.Status is "completed" or "cancelled" or "canceled"
      )
        return Fail("This load is no longer active.", 409);
      if (
        update.Revision < 0
        || update.Revision == long.MaxValue
        || update.Revision != stop.OperationRevision
        || update.StopIdentity
          != StopCompletionIdentity.Create(
            stop.Id,
            stop.Sequence,
            stop.Job,
            stop.Address,
            stop.City,
            stop.Province,
            stop.Country,
            stop.Name,
            stop.TruckId,
            stop.ScheduledDate,
            stop.ScheduledTime
          )
      )
        return Fail("This stop changed. Reload the load.", 409);
      if (
        update.StateAfter == "No truck"
        && load.PlanningFromStopId is null
        && StopOperation
          .Resolve(load.Stops, null)
          .Any(s => s.Sequence < stop.Sequence && s.StateAfter != "No truck")
      )
        return Fail(
          "A personal-travel leg after truck driving requires a separate truck assignment.",
          409
        );
      var anchor = load.Stops.FirstOrDefault(s =>
        s.Id == load.PlanningFromStopId
      );
      if (
        update.StateAfter is not null and not "No truck"
          && anchor is not null
          && stop.Sequence < anchor.Sequence
        || update.StateAfter == "No truck"
          && anchor is not null
          && stop.Sequence >= anchor.Sequence
      )
        return Fail(
          "Review the truck starting stop before changing this operation.",
          409
        );
      stop.ManualAction = update.Action;
      stop.ManualStateAfter = update.StateAfter;
      stop.OperationRevision++;
      stop.OperationRecordedAt = clock.GetUtcNow().UtcDateTime;
      stop.OperationRecordedBy = actor.Id;
      if (leg is not null)
      {
        var source = load.Stops.SingleOrDefault(x => x.Id == stop.Id);
        if (update.Action is null)
        {
          if (source is null)
            return Fail("Choose an explicit operation for this visit.", 400);
          stop.Job = source.Job;
        }
        else
          stop.StateAfter = update.StateAfter!;
        if (source is not null)
        {
          source.ManualAction = stop.ManualAction;
          source.ManualStateAfter = stop.ManualStateAfter;
          source.OperationRevision = stop.OperationRevision;
          source.OperationRecordedAt = stop.OperationRecordedAt;
          source.OperationRecordedBy = stop.OperationRecordedBy;
        }
        if (update.Action is null)
          stop.StateAfter = StopOperation
            .Resolve(load.Stops, load.PlanningFromStopId)
            .Single(x => x.Id == stop.Id)
            .StateAfter;
        await DispatchAcceptedStopChange.ApplyAsync(
          db,
          leg,
          stop,
          "stop-operation-corrected",
          actor.Id,
          stop.OperationRecordedAt.Value,
          ct
        );
      }
      try
      {
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
      }
      catch (DbUpdateConcurrencyException)
      {
        db.Entry(stop).State = EntityState.Detached;
        return Fail("This stop changed. Reload the load.", 409);
      }
      reads.Invalidate("dispatch");
      reads.Invalidate("board");
      reads.Invalidate("execution");
      if (leg is not null)
        reads.Invalidate($"route:{load.Id}:leg:{leg.Id}");
      reads.Invalidate("route-previews");
      reads.Invalidate($"route:{load.Id}");
      preparation.MarkDirty(load.Id);
      var trucks = load
        .Stops.Where(s => s.TruckId.HasValue)
        .Select(s => s.TruckId!.Value)
        .ToHashSet();
      if (leg is not null)
        trucks.Add(leg.TruckId);
      if ((load.PlanningTruckId ?? load.TruckId) is { } truck)
        trucks.Add(truck);
      foreach (var id in trucks)
        preparation.MarkTruckDirty(id);
      logger.LogInformation(
        "Stop operation changed for {DispatchId}/{StopId} by {ActorId}, revision {Revision}",
        load.Id,
        stop.Id,
        actor.Id,
        stop.OperationRevision
      );
      return RequestResponse<StopOperationState>.Ok(
        new(
          stop.ManualAction,
          stop.ManualStateAfter,
          stop.OperationRevision,
          stop.OperationRecordedAt
        )
      );
    }
    catch (Exception ex) when (db.IsWriteConflict(ex))
    {
      return Fail("This execution changed. Reload the load.", 409);
    }
    finally
    {
      ProcessGates.Dispatch.Release();
    }
  }

  private static RequestResponse<StopOperationState> Fail(
    string message,
    int status = 400
  ) => RequestResponse<StopOperationState>.Fail(message, status);
}
