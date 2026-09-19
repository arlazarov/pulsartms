using Application.Features.Mileage.Models;
using Application.Features.Mileage.Services;
using Application.Models;
using Domain.Entities.Mileage;

namespace Application.Features.Mileage.Queries;

public sealed record GetMileagePolicyQuery
  : IRequest<RequestResponse<MileagePolicyState>>;

public sealed class GetMileagePolicyHandler(
  IAppDbContext db,
  ICurrentUser caller,
  IUserRoleService roles
) : IRequestHandler<GetMileagePolicyQuery, RequestResponse<MileagePolicyState>>
{
  public async Task<RequestResponse<MileagePolicyState>> Handle(
    GetMileagePolicyQuery request,
    CancellationToken ct
  )
  {
    if (await MileageAccess.ActorAsync(db, caller, roles, false, ct) is null)
      return RequestResponse<MileagePolicyState>.Fail("Access denied.", 403);
    var policy =
      await db
        .MileageAllocationPolicies.AsNoTracking()
        .SingleOrDefaultAsync(
          x => x.Id == MileageAllocationPolicy.SingletonId,
          ct
        ) ?? new();
    return RequestResponse<MileagePolicyState>.Ok(
      MileagePolicyView.From(policy)
    );
  }
}

internal static class MileagePolicyView
{
  public static MileagePolicyState From(MileageAllocationPolicy policy) =>
    new(
      policy.Revision,
      policy.YardReturn,
      policy.Home,
      policy.Maintenance,
      policy.Reposition,
      policy.UpdatedAt
    );
}
