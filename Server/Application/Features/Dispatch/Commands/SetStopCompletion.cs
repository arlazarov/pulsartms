using System.Data;
using Application.Caching;
using Application.Concurrency;
using Application.Features.Dispatch.Models;
using Application.Features.Dispatch.Services;
using Application.Features.Routing.Background;
using Application.Features.Routing.Services.Routes;
using Application.Models;
using Domain.Entities.Dispatch;
using Domain.Rules;

namespace Application.Features.Dispatch.Commands;

public sealed record StopCompletionUpdate(
  DateTimeOffset? CompletedAt,
  long Revision,
  string CompletionIdentity
);

public sealed record StopCompletionState(
  DateTime? CompletedAt,
  Guid? CompletedBy,
  string? CompletedByName,
  DateTime? RecordedAt,
  long Revision,
  // Whether the stop now counts as done - not always what was just asked
  // for: one waiting for a handoff is not done however it is marked.
  bool IsCompleted = false
);

public sealed record SetStopCompletionCommand(
  Guid DispatchId,
  Guid StopId,
  StopCompletionUpdate Update
) : IRequest<RequestResponse<StopCompletionState>>;

public sealed class SetStopCompletionValidator
  : AbstractValidator<SetStopCompletionCommand>
{
  public SetStopCompletionValidator()
  {
    RuleFor(x => x.DispatchId).NotEmpty();
    RuleFor(x => x.StopId).NotEmpty();
    RuleFor(x => x.Update).NotNull();
    When(
      x => x.Update is not null,
      () =>
      {
        RuleFor(x => x.Update.Revision)
          .GreaterThanOrEqualTo(0)
          .LessThan(long.MaxValue);
        RuleFor(x => x.Update.CompletionIdentity).NotEmpty().Length(64);
      }
    );
  }
}

public sealed class SetStopCompletionHandler(
  IAppDbContext db,
  ICurrentUser caller,
  IUserRoleService roles,
  TimeProvider clock,
  ReadCache reads,
  RoutePreparationQueue preparation,
  RoutePlanningService routes
)
  : IRequestHandler<
    SetStopCompletionCommand,
    RequestResponse<StopCompletionState>
  >
{
  public async Task<RequestResponse<StopCompletionState>> Handle(
    SetStopCompletionCommand request,
    CancellationToken ct
  )
  {
    if (
      !caller.IsAuthenticated
      || string.IsNullOrWhiteSpace(caller.IdentityUserId)
    )
      return RequestResponse<StopCompletionState>.Fail("Unauthorized.", 401);
    var actor = await db
      .Users.AsNoTracking()
      .SingleOrDefaultAsync(
        u => u.IdentityUserId == caller.IdentityUserId && u.IsActive,
        ct
      );
    if (
      actor is null
      || await roles.GetAsync(caller.IdentityUserId, ct)
        is not ("Admin" or "Dispatch")
    )
      return RequestResponse<StopCompletionState>.Fail(
        "You cannot update stop completion.",
        403
      );
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
      var stop = state?.EffectiveStops.GetValueOrDefault(request.StopId);
      if (state is null || stop is null)
        return RequestResponse<StopCompletionState>.Fail(
          "Stop not found.",
          404
        );
      var row = state.Response.Stops.Single(x => x.Id == stop.Id);
      var leg = state.Legs.SingleOrDefault(x => x.Id == row.ExecutionLegId);
      if (leg is not null && (!row.CanCorrect || row.Transfer is not null))
        return RequestResponse<StopCompletionState>.Fail(
          "Use the transfer workflow for this execution visit.",
          409
        );
      stop.Dispatch = state.Load;
      var update = request.Update;
      if (
        update?.CompletedAt is not null
        && leg is { Status: "planned", StartSwitchId: not null }
      )
        return RequestResponse<StopCompletionState>.Fail(
          "Confirm receipt before recording this assignment's work.",
          409
        );
      if (stop.CompletionOverride.HasValue)
        return RequestResponse<StopCompletionState>.Fail(
          "This stop has a correction. Use the stop correction editor.",
          409
        );
      if (
        update is null
        || update.Revision < 0
        || update.Revision == long.MaxValue
      )
        return Conflict();
      var identity = StopCompletionIdentity.Create(
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
      );
      if (
        update.Revision != stop.ManualCompletionRevision
        || update.CompletionIdentity != identity
      )
        return Conflict();
      var now = clock.GetUtcNow().UtcDateTime;
      var completed = update.CompletedAt?.UtcDateTime;
      if (
        completed.HasValue
        && (
          completed > now
          || completed < new DateTime(2000, 1, 1, 0, 0, 0, DateTimeKind.Utc)
          || stop.ArrivedAt.HasValue && completed < stop.ArrivedAt
        )
      )
        return RequestResponse<StopCompletionState>.Fail(
          "Choose an actual completion time after arrival and not in the future."
        );
      if (
        completed.HasValue
        && (stop.DepartedAt ?? stop.PickedUpAt ?? stop.DeliveredAt).HasValue
      )
        return RequestResponse<StopCompletionState>.Fail(
          "The source has already confirmed this stop. Reload the load.",
          409
        );
      if (
        completed.HasValue
        && stop.Dispatch.Status is "completed" or "cancelled" or "canceled"
      )
        return RequestResponse<StopCompletionState>.Fail(
          "This load is no longer active.",
          409
        );
      if (stop.ManualCompletedAt == completed)
        return RequestResponse<StopCompletionState>.Ok(State(stop));
      var trucks = await db
        .DispatchStops.AsNoTracking()
        .Where(s => s.DispatchId == stop.DispatchId && s.TruckId != null)
        .Select(s => s.TruckId!.Value)
        .Distinct()
        .ToListAsync(ct);
      if (stop.Dispatch.TruckId is { } truck)
        trucks.Add(truck);
      stop.ManualCompletedAt = completed;
      stop.ManualCompletedBy = completed.HasValue ? actor.Id : null;
      stop.ManualCompletedByName = completed.HasValue ? actor.Name : null;
      stop.ManualCompletionRecordedAt = completed.HasValue ? now : null;
      stop.ManualCompletionRevision++;
      var change = new DispatchStopCompletionEvent
      {
        Id = Guid.NewGuid(),
        DispatchId = stop.DispatchId,
        StopId = stop.Id,
        Revision = stop.ManualCompletionRevision,
        CompletedAt = completed,
        RecordedAt = now,
        ActorId = actor.Id,
      };
      db.DispatchStopCompletionEvents.Add(change);
      if (leg is not null)
      {
        if (
          state.Load.Stops.SingleOrDefault(x => x.Id == stop.Id) is { } source
        )
        {
          source.ManualCompletedAt = stop.ManualCompletedAt;
          source.ManualCompletedBy = stop.ManualCompletedBy;
          source.ManualCompletedByName = stop.ManualCompletedByName;
          source.ManualCompletionRecordedAt = stop.ManualCompletionRecordedAt;
          source.ManualCompletionRevision = stop.ManualCompletionRevision;
        }
        await DispatchAcceptedStopChange.ApplyAsync(
          db,
          leg,
          stop,
          "stop-completion-recorded",
          actor.Id,
          now,
          ct
        );
        trucks.Add(leg.TruckId);
      }
      var route = leg is null
        ? await routes.PrepareCompletionAsync(stop, ct)
        : null;
      try
      {
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
      }
      catch (DbUpdateConcurrencyException)
      {
        db.Entry(stop).State = EntityState.Detached;
        db.Entry(change).State = EntityState.Detached;
        if (route is not null)
          db.Entry(route).State = EntityState.Detached;
        return Conflict();
      }
      catch (DbUpdateException)
      {
        db.Entry(stop).State = EntityState.Detached;
        db.Entry(change).State = EntityState.Detached;
        if (route is not null)
          db.Entry(route).State = EntityState.Detached;
        var revision = await db
          .DispatchStops.AsNoTracking()
          .Where(s => s.Id == request.StopId)
          .Select(s => (long?)s.ManualCompletionRevision)
          .SingleOrDefaultAsync(ct);
        if (revision != update.Revision)
          return Conflict();
        throw;
      }
      reads.Invalidate("dispatch");
      reads.Invalidate("board");
      reads.Invalidate("execution");
      if (leg is not null)
        reads.Invalidate($"route:{stop.DispatchId}:leg:{leg.Id}");
      reads.Invalidate("route-previews");
      reads.Invalidate($"route:{stop.DispatchId}");
      preparation.MarkDirty(stop.DispatchId);
      foreach (var id in trucks.Distinct())
        preparation.MarkTruckDirty(id);
      return RequestResponse<StopCompletionState>.Ok(State(stop));
    }
    catch (Exception ex) when (db.IsWriteConflict(ex))
    {
      return Conflict();
    }
    finally
    {
      ProcessGates.Dispatch.Release();
    }
  }

  private static StopCompletionState State(DispatchStop stop) =>
    new(
      stop.ManualCompletedAt,
      stop.ManualCompletedBy,
      stop.ManualCompletedByName,
      stop.ManualCompletionRecordedAt,
      stop.ManualCompletionRevision,
      stop.IsCompleted
    );

  private static RequestResponse<StopCompletionState> Conflict() =>
    RequestResponse<StopCompletionState>.Fail(
      "This stop changed in another session. Reload the load before saving.",
      409
    );
}
