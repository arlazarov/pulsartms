using System.Linq.Expressions;
using Domain.Entities.Dispatch;

namespace Application.Features.Dispatch.Activity;

internal static class DispatchActivityAccess
{
  public static async Task<User?> ActorAsync(
    IAppDbContext db,
    ICurrentUser caller,
    IUserRoleService roles,
    CancellationToken ct
  )
  {
    if (
      !caller.IsAuthenticated
      || string.IsNullOrWhiteSpace(caller.IdentityUserId)
      || await roles.GetAsync(caller.IdentityUserId, ct)
        is not ("Admin" or "Dispatch")
    )
      return null;
    return await db
      .Users.AsNoTracking()
      .SingleOrDefaultAsync(
        x => x.IdentityUserId == caller.IdentityUserId && x.IsActive,
        ct
      );
  }

  public static readonly Expression<
    Func<DispatchActivityEntry, DispatchActivityItem>
  > Projection = x =>
    new(
      x.Id,
      x.DispatchId,
      x.CreatedRevision,
      x.Revision,
      x.Kind,
      x.Text,
      x.StopId,
      x.StopLabel,
      x.DriverId,
      x.DriverName,
      x.ActorId,
      x.ActorName,
      x.RecordedAt,
      x.NeedsAttention,
      x.ResolvedBy,
      x.ResolvedByName,
      x.ResolvedAt
    );

  public static DispatchActivityItem Item(DispatchActivityEntry x) =>
    new(
      x.Id,
      x.DispatchId,
      x.CreatedRevision,
      x.Revision,
      x.Kind,
      x.Text,
      x.StopId,
      x.StopLabel,
      x.DriverId,
      x.DriverName,
      x.ActorId,
      x.ActorName,
      x.RecordedAt,
      x.NeedsAttention,
      x.ResolvedBy,
      x.ResolvedByName,
      x.ResolvedAt
    );
}
