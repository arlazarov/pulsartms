using Application.Features.DriverGroups.Services;
using Application.Models;

namespace Application.Features.DriverGroups.Queries;

// The signed-in dispatcher's own groups and the one they have chosen (null
// for all drivers). Another dispatcher's groups are never listed.
public sealed record GetDriverGroupsQuery
  : IRequest<RequestResponse<DriverGroupsView>>;

public sealed record DriverGroupsView(
  Guid? Selected,
  IReadOnlyList<DriverGroupView> Groups
);

public sealed record DriverGroupView(
  Guid Id,
  string Name,
  long Revision,
  IReadOnlyList<Guid> Drivers
);

public sealed class GetDriverGroupsHandler(
  IAppDbContext db,
  ICurrentUser caller
) : IRequestHandler<GetDriverGroupsQuery, RequestResponse<DriverGroupsView>>
{
  public async Task<RequestResponse<DriverGroupsView>> Handle(
    GetDriverGroupsQuery request,
    CancellationToken ct
  )
  {
    if (await DriverGroupOwner.UserAsync(db, caller, ct) is not { } user)
      return RequestResponse<DriverGroupsView>.Fail("Access denied.", 403);
    var selected = await db
      .Users.AsNoTracking()
      .Where(x => x.Id == user)
      .Select(x => x.SelectedDriverGroupId)
      .SingleAsync(ct);
    var groups = await db
      .DriverGroups.AsNoTracking()
      .Where(x => x.OwnerUserId == user)
      .OrderBy(x => x.Name)
      .Select(x => new DriverGroupView(
        x.Id,
        x.Name,
        x.Revision,
        db.DriverGroupMembers.Where(m => m.GroupId == x.Id)
          .Select(m => m.DriverId)
          .ToList()
      ))
      .ToListAsync(ct);
    return RequestResponse<DriverGroupsView>.Ok(new(selected, groups));
  }
}
