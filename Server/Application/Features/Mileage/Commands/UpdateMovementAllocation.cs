using Application.Features.Mileage.Models;
using Application.Features.Mileage.Services;
using Application.Models;
using Domain.Entities.Mileage;

namespace Application.Features.Mileage.Commands;

public sealed record UpdateMovementAllocationCommand(
  Guid MovementId,
  MileageAllocationUpdate Update
) : IRequest<RequestResponse<MileageMovementRow>>;

public sealed class UpdateMovementAllocationHandler(
  IAppDbContext db,
  ICurrentUser caller,
  IUserRoleService roles,
  TimeProvider clock
)
  : IRequestHandler<
    UpdateMovementAllocationCommand,
    RequestResponse<MileageMovementRow>
  >
{
  public async Task<RequestResponse<MileageMovementRow>> Handle(
    UpdateMovementAllocationCommand command,
    CancellationToken ct
  )
  {
    var actor = await MileageAccess.ActorAsync(db, caller, roles, false, ct);
    if (actor is null)
      return RequestResponse<MileageMovementRow>.Fail("Access denied.", 403);
    var update = command.Update;
    if (
      update is null
      || update.Revision is < 1 or long.MaxValue
      || update.Target
        is not (
          "automatic"
          or "previous"
          or "next"
          or "carried"
          or "unallocated"
        )
      || string.IsNullOrWhiteSpace(update.Reason)
      || update.Reason.Trim().Length > 400
    )
      return RequestResponse<MileageMovementRow>.Fail(
        "Choose an allocation and enter a reason of at most 400 characters.",
        400
      );
    var movement = await db.Movements.SingleOrDefaultAsync(
      x => x.Id == command.MovementId,
      ct
    );
    if (movement is null)
      return RequestResponse<MileageMovementRow>.Fail(
        "Movement not found.",
        404
      );
    if (movement.Revision != update.Revision || movement.PlannedSuperseded)
      return MileageMutation.Conflict();
    var policy =
      await db
        .MileageAllocationPolicies.AsNoTracking()
        .SingleOrDefaultAsync(
          x => x.Id == MileageAllocationPolicy.SingletonId,
          ct
        ) ?? new();
    var manual = update.Target != "automatic";
    var allocation = manual
      ? MileageAllocation.ForTarget(
        movement,
        update.Target,
        update.Reason.Trim()
      )
      : MileageAllocation.Resolve(movement, policy);
    if (
      manual
      && update.Target != "unallocated"
      && allocation.DispatchId is null
    )
      return RequestResponse<MileageMovementRow>.Fail(
        "This movement has no load for that allocation target.",
        400
      );
    if (!manual)
      allocation = allocation with
      {
        Reason = $"{allocation.Reason}; {update.Reason.Trim()}",
      };
    movement.Revision++;
    var history = MileageMutation.Allocate(
      movement,
      allocation,
      policy.Revision,
      manual,
      actor.Value,
      clock.GetUtcNow().UtcDateTime
    );
    db.MovementAllocationEvents.Add(history);
    return await MileageMutation.SaveAsync(db, movement, history, ct);
  }
}
