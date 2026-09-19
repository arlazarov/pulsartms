using Application.Features.Mileage.Models;
using Application.Features.Mileage.Queries;
using Application.Features.Mileage.Services;
using Application.Models;
using Domain.Entities.Mileage;

namespace Application.Features.Mileage.Commands;

public sealed record UpdateMileagePolicyCommand(MileagePolicyUpdate Update)
  : IRequest<RequestResponse<MileagePolicyState>>;

public sealed class UpdateMileagePolicyHandler(
  IAppDbContext db,
  ICurrentUser caller,
  IUserRoleService roles,
  TimeProvider clock
)
  : IRequestHandler<
    UpdateMileagePolicyCommand,
    RequestResponse<MileagePolicyState>
  >
{
  public async Task<RequestResponse<MileagePolicyState>> Handle(
    UpdateMileagePolicyCommand command,
    CancellationToken ct
  )
  {
    var actor = await MileageAccess.ActorAsync(db, caller, roles, true, ct);
    if (actor is null)
      return RequestResponse<MileagePolicyState>.Fail(
        "Administrator access is required.",
        403
      );
    var update = command.Update;
    if (
      update is null
      || update.Revision is < 0 or long.MaxValue
      || !MileageAllocation.ValidPolicyTarget(update.YardReturn)
      || !MileageAllocation.ValidPolicyTarget(update.Home)
      || !MileageAllocation.ValidPolicyTarget(update.Maintenance)
      || !MileageAllocation.ValidPolicyTarget(update.Reposition)
    )
      return RequestResponse<MileagePolicyState>.Fail(
        "Choose previous load, next load or unallocated for each purpose.",
        400
      );
    var policy = await db.MileageAllocationPolicies.SingleOrDefaultAsync(
      x => x.Id == MileageAllocationPolicy.SingletonId,
      ct
    );
    if (update.Revision != (policy?.Revision ?? 0))
      return Conflict();
    var creating = policy is null;
    policy ??= new() { Id = MileageAllocationPolicy.SingletonId };
    if (creating)
      db.MileageAllocationPolicies.Add(policy);
    policy.YardReturn = update.YardReturn;
    policy.Home = update.Home;
    policy.Maintenance = update.Maintenance;
    policy.Reposition = update.Reposition;
    policy.Revision++;
    policy.UpdatedAt = clock.GetUtcNow().UtcDateTime;
    policy.UpdatedBy = actor;
    try
    {
      await db.SaveChangesAsync(ct);
    }
    catch (Exception exception) when (db.IsWriteConflict(exception))
    {
      db.Entry(policy).State = EntityState.Detached;
      return Conflict();
    }
    return RequestResponse<MileagePolicyState>.Ok(
      MileagePolicyView.From(policy)
    );
  }

  private static RequestResponse<MileagePolicyState> Conflict() =>
    RequestResponse<MileagePolicyState>.Fail(
      "Mileage policy changed. Reload before saving.",
      409
    );
}
