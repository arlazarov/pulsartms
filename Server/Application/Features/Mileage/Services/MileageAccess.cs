namespace Application.Features.Mileage.Services;

internal static class MileageAccess
{
  public static async Task<Guid?> ActorAsync(
    IAppDbContext db,
    ICurrentUser caller,
    IUserRoleService roles,
    bool admin,
    CancellationToken ct
  )
  {
    if (
      !caller.IsAuthenticated
      || string.IsNullOrWhiteSpace(caller.IdentityUserId)
    )
      return null;
    var role = await roles.GetAsync(caller.IdentityUserId, ct);
    if (role != "Admin" && (admin || role != "Dispatch"))
      return null;
    return await db
      .Users.AsNoTracking()
      .Where(x => x.IdentityUserId == caller.IdentityUserId && x.IsActive)
      .Select(x => (Guid?)x.Id)
      .SingleOrDefaultAsync(ct);
  }
}
