using Application.Features.Fleet.Services;
using Application.Models;
using Domain.Models.Fleet;

namespace Application.Features.Fleet.Queries;

public sealed record GetDriverContactQuery(Guid DriverId)
  : IRequest<RequestResponse<DriverContactState>>;

public sealed class GetDriverContactHandler(
  IAppDbContext db,
  ICurrentUser caller,
  IUserRoleService roles
) : IRequestHandler<GetDriverContactQuery, RequestResponse<DriverContactState>>
{
  public async Task<RequestResponse<DriverContactState>> Handle(
    GetDriverContactQuery request,
    CancellationToken ct
  )
  {
    if (!await DriverContacts.MayUseAsync(db, caller, roles, ct))
      return RequestResponse<DriverContactState>.Fail(
        "You cannot see driver contacts.",
        403
      );
    var driver = await db
      .Drivers.AsNoTracking()
      .SingleOrDefaultAsync(x => x.Id == request.DriverId, ct);
    return driver is null
      ? RequestResponse<DriverContactState>.Fail("Driver not found.", 404)
      : RequestResponse<DriverContactState>.Ok(DriverContacts.State(driver));
  }
}
