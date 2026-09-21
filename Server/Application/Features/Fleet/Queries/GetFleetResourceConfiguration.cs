using Application.Features.Fleet.Services;
using Application.Models;
using Domain.Models.Fleet;

namespace Application.Features.Fleet.Queries;

public sealed record GetFleetResourceConfigurationQuery(string Kind, Guid Id)
  : IRequest<RequestResponse<FleetConfigurationState>>;

public sealed class GetFleetResourceConfigurationHandler(
  IAppDbContext db,
  ICurrentUser caller,
  IUserRoleService roles
)
  : IRequestHandler<
    GetFleetResourceConfigurationQuery,
    RequestResponse<FleetConfigurationState>
  >
{
  public async Task<RequestResponse<FleetConfigurationState>> Handle(
    GetFleetResourceConfigurationQuery request,
    CancellationToken ct
  )
  {
    if (!await FleetConfigurationAccess.IsAdminAsync(db, caller, roles, ct))
      return RequestResponse<FleetConfigurationState>.Fail(
        "Administrator access is required.",
        403
      );
    var resource = await FleetConfigurationReader.FindAsync(
      db,
      request.Kind,
      request.Id,
      ct
    );
    if (resource is null)
      return RequestResponse<FleetConfigurationState>.Fail(
        "Fleet resource not found.",
        404
      );
    return RequestResponse<FleetConfigurationState>.Ok(
      await FleetConfigurationReader.StateAsync(db, resource, ct)
    );
  }
}
