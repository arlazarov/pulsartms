using Application.Features.Mileage.Models;
using Application.Models;
using Domain.Entities.Mileage;

namespace Application.Features.Mileage.Services;

internal static class MileageMutation
{
  public static MovementAllocationEvent Allocate(
    Movement movement,
    MovementAllocation allocation,
    long policyRevision,
    bool manual,
    Guid actor,
    DateTime now
  )
  {
    var history = new MovementAllocationEvent
    {
      Id = Guid.NewGuid(),
      MovementId = movement.Id,
      Revision = movement.Revision,
      PreviousAllocationDispatchId = movement.AllocatedDispatchId,
      AllocatedDispatchId = allocation.DispatchId,
      Target = allocation.Target,
      Reason = allocation.Reason,
      PolicyRevision = policyRevision,
      ManualOverride = manual,
      RecordedAt = now,
      RecordedBy = actor,
    };
    movement.AllocatedDispatchId = allocation.DispatchId;
    movement.AllocationTarget = allocation.Target;
    movement.AllocationReason = allocation.Reason;
    movement.PolicyRevision = policyRevision;
    movement.ManualOverride = manual;
    return history;
  }

  public static async Task<RequestResponse<MileageMovementRow>> SaveAsync(
    IAppDbContext db,
    Movement movement,
    object history,
    CancellationToken ct
  )
  {
    try
    {
      await db.SaveChangesAsync(ct);
    }
    catch (Exception exception) when (db.IsWriteConflict(exception))
    {
      db.Entry(movement).State = EntityState.Detached;
      db.Entry(history).State = EntityState.Detached;
      return Conflict();
    }
    return RequestResponse<MileageMovementRow>.Ok(
      (
        await MileageMovementView.WithNumbersAsync(
          db,
          [MileageMovementView.From(movement)],
          ct
        )
      )[0]
    );
  }

  public static RequestResponse<MileageMovementRow> Conflict() =>
    RequestResponse<MileageMovementRow>.Fail(
      "Movement changed. Reload its evidence and allocation before saving.",
      409
    );
}
