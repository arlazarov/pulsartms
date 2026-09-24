namespace Application.Features.Messaging.Services;

// Messaging's settings are an active administrator's.
public static class MessagingAdmin
{
  public const string Required = "Administrator access is required.";

  public static async Task<Guid?> UserAsync(
    IAppDbContext db,
    ICurrentUser caller,
    IUserRoleService roles,
    CancellationToken ct
  ) =>
    !caller.IsAuthenticated
    || string.IsNullOrEmpty(caller.IdentityUserId)
    || await roles.GetAsync(caller.IdentityUserId, ct) != "Admin"
      ? null
      : await db
        .Users.AsNoTracking()
        .Where(x => x.IdentityUserId == caller.IdentityUserId && x.IsActive)
        .Select(x => (Guid?)x.Id)
        .SingleOrDefaultAsync(ct);
}
