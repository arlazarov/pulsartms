using System.Data;
using Application.Caching;
using Application.Features.Dispatch.Models;
using Application.Features.Execution.Models;
using Application.Features.Execution.Services;
using Application.Features.Routing.Background;
using Application.Features.Synchronization.Services;
using Application.Models;
using Domain.Entities.Dispatch;
using Microsoft.Extensions.Logging;

namespace Application.Features.Dispatch.Commands;

public sealed record TruckAssignmentUpdate(
  string? TruckNumber,
  Guid? FromStopId,
  long Revision,
  string? StopIdentity
);

public sealed record TruckAssignmentState(
  Guid? TruckId,
  Guid? FromStopId,
  long Revision,
  DateTime? RecordedAt
);

public sealed record SetTruckAssignmentCommand(
  Guid DispatchId,
  TruckAssignmentUpdate Update
) : IRequest<RequestResponse<TruckAssignmentState>>;

public sealed class SetTruckAssignmentValidator
  : AbstractValidator<SetTruckAssignmentCommand>
{
  public SetTruckAssignmentValidator()
  {
    RuleFor(x => x.DispatchId).NotEmpty();
    RuleFor(x => x.Update).NotNull();
    When(
      x => x.Update is not null,
      () =>
      {
        RuleFor(x => x.Update.Revision).InclusiveBetween(0, long.MaxValue - 1);
        RuleFor(x => x.Update.TruckNumber).MaximumLength(50);
      }
    );
  }
}

public sealed class SetTruckAssignmentHandler(
  IAppDbContext db,
  ICurrentUser caller,
  IUserRoleService roles,
  TimeProvider clock,
  ReadCache reads,
  RoutePreparationQueue preparation,
  ILogger<SetTruckAssignmentHandler> logger
)
  : IRequestHandler<
    SetTruckAssignmentCommand,
    RequestResponse<TruckAssignmentState>
  >
{
  public async Task<RequestResponse<TruckAssignmentState>> Handle(
    SetTruckAssignmentCommand request,
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
      return Fail("You cannot change truck assignments.", 403);
    await SynchronizationGates.Dispatch.WaitAsync(ct);
    try
    {
      await using var transaction = await db.Database.BeginTransactionAsync(
        IsolationLevel.Serializable,
        ct
      );
      var load = await db
        .Dispatches.Include(x => x.Stops)
        .SingleOrDefaultAsync(x => x.Id == request.DispatchId, ct);
      if (load is null)
        return Fail("Load not found.", 404);
      if (await db.LoadExecutionLegs.AnyAsync(x => x.DispatchId == load.Id, ct))
        return Fail(
          "This load has accepted execution. Use the assignment correction editor.",
          409
        );
      var update = request.Update;
      if (
        update is null
        || update.Revision < 0
        || update.Revision == long.MaxValue
        || update.Revision != load.PlanningAssignmentRevision
      )
        return Fail("Assignment changed. Reload the load.", 409);
      Guid? truckId = null;
      if (update.FromStopId.HasValue)
      {
        if (load.Status is "completed" or "cancelled" or "canceled")
          return Fail("This load is no longer active.", 409);
        var stop = load.Stops.SingleOrDefault(x => x.Id == update.FromStopId);
        if (
          stop is null
          || StopCompletionIdentity.Create(
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
          ) != update.StopIdentity
        )
          return Fail("Starting stop changed. Reload the load.", 409);
        var number = update.TruckNumber?.Trim();
        truckId = await db
          .Trucks.Where(x => x.UnitNumber == number && x.IsActive)
          .Select(x => (Guid?)x.Id)
          .SingleOrDefaultAsync(ct);
        if (truckId is null)
          return Fail("Choose an active truck.");
        if (
          load.TruckId.HasValue && load.TruckId != truckId
          || !string.IsNullOrWhiteSpace(load.TruckNumber)
            && !load
              .TruckNumber.Trim()
              .Equals(number, StringComparison.OrdinalIgnoreCase)
          || StopOperation
            .Resolve(load.Stops, update.FromStopId)
            .Where(s => s.StateAfter != "No truck")
            .Any(s =>
              s.TruckId.HasValue && s.TruckId != truckId
              || !string.IsNullOrWhiteSpace(s.TruckNumber)
                && !s.TruckNumber.Equals(
                  number,
                  StringComparison.OrdinalIgnoreCase
                )
              || s.Sequence < stop.Sequence
                && (
                  s.TruckId.HasValue
                  || !string.IsNullOrWhiteSpace(s.TruckNumber)
                )
            )
        )
          return Fail(
            "The imported truck assignments conflict with this starting stop. Review the source assignments.",
            409
          );
      }
      else if (!string.IsNullOrWhiteSpace(update.TruckNumber))
        return Fail("Choose the truck starting stop.");
      var previous = load.PlanningTruckId ?? load.TruckId;
      load.PlanningTruckId = truckId;
      load.PlanningFromStopId = update.FromStopId;
      load.PlanningAssignmentRecordedAt = clock.GetUtcNow().UtcDateTime;
      load.PlanningAssignmentRecordedBy = actor.Id;
      load.PlanningAssignmentRevision++;
      if (truckId.HasValue && load.Status is "unassigned" or "planned")
        load.Status = "assigned";
      if (truckId.HasValue)
      {
        var stops = StopOperation
          .Resolve(load.Stops, update.FromStopId)
          .Select(ExecutionSnapshots.Copy)
          .ToArray();
        var start = Array.FindIndex(stops, x => x.Id == update.FromStopId);
        for (var i = 0; i < stops.Length; i++)
        {
          if (i < start)
            stops[i].StateAfter = "No truck";
          else
            stops[i].TruckId = truckId;
        }
        await InitialExecutionAssignment.AcceptAsync(
          db,
          load,
          stops,
          actor.Id,
          null,
          load.PlanningAssignmentRecordedAt.Value,
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
        db.Entry(load).State = EntityState.Detached;
        return Fail("Assignment changed. Reload the load.", 409);
      }
      reads.Invalidate("dispatch");
      reads.Invalidate("board");
      reads.Invalidate("execution");
      reads.Invalidate("route-previews");
      reads.Invalidate($"route:{load.Id}");
      preparation.MarkDirty(load.Id);
      if (previous is { } oldTruck)
        preparation.MarkTruckDirty(oldTruck);
      if (truckId is { } newTruck)
        preparation.MarkTruckDirty(newTruck);
      logger.LogInformation(
        "Dispatch truck assignment confirmed for {DispatchId} by {ActorId}: truck {TruckId}, start {StopId}, revision {Revision}",
        load.Id,
        actor.Id,
        truckId,
        update.FromStopId,
        load.PlanningAssignmentRevision
      );
      return RequestResponse<TruckAssignmentState>.Ok(
        new(
          truckId,
          update.FromStopId,
          load.PlanningAssignmentRevision,
          load.PlanningAssignmentRecordedAt
        )
      );
    }
    catch (Exception ex) when (db.IsWriteConflict(ex))
    {
      return Fail("Assignment changed. Reload the load.", 409);
    }
    finally
    {
      SynchronizationGates.Dispatch.Release();
    }
  }

  private static RequestResponse<TruckAssignmentState> Fail(
    string message,
    int status = 400
  ) => RequestResponse<TruckAssignmentState>.Fail(message, status);
}
