namespace Application.Features.Fleet.Services;

internal static class FleetConfigurationAccess
{
  public static async Task<bool> IsAdminAsync(
    IAppDbContext db,
    ICurrentUser caller,
    IUserRoleService roles,
    CancellationToken ct
  ) =>
    caller.IsAuthenticated
    && !string.IsNullOrEmpty(caller.IdentityUserId)
    && await roles.GetAsync(caller.IdentityUserId, ct) == "Admin"
    && await db
      .Users.AsNoTracking()
      .AnyAsync(
        x => x.IdentityUserId == caller.IdentityUserId && x.IsActive,
        ct
      );

  public static bool ValidKind(string kind) =>
    kind is "trucks" or "trailers" or "drivers";
}
