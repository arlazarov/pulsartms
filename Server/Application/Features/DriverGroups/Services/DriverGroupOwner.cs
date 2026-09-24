namespace Application.Features.DriverGroups.Services;

// Groups are the signed-in dispatcher's own: every read and write starts
// from their active user row, and a group of anyone else is not found.
public static class DriverGroupOwner
{
  public static Task<Guid?> UserAsync(
    IAppDbContext db,
    ICurrentUser caller,
    CancellationToken ct
  ) =>
    string.IsNullOrEmpty(caller.IdentityUserId)
      ? Task.FromResult<Guid?>(null)
      : db
        .Users.AsNoTracking()
        .Where(x => x.IdentityUserId == caller.IdentityUserId && x.IsActive)
        .Select(x => (Guid?)x.Id)
        .SingleOrDefaultAsync(ct);
}
