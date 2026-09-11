using Application.Interfaces;
using Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace Infrastructure.Identity;

public sealed class UserRoleService(AppDbContext db) : IUserRoleService
{
  private const string RoleClaim = "amftms:role";
  public async Task<string?> GetAsync(string identityId, CancellationToken ct = default)
  {
    if (!await db.Users.AnyAsync(u => u.IdentityUserId == identityId && u.IsActive, ct)) return null;
    var roles = await db.UserClaims.Where(c => c.UserId == identityId && c.ClaimType == RoleClaim)
      .Select(c => c.ClaimValue).ToListAsync(ct);
    return Resolve(roles);
  }

  public async Task<Dictionary<Guid, string>> GetAsync(IReadOnlyCollection<Guid> ids, CancellationToken ct = default)
  {
    var users = await db.Users.AsNoTracking().Where(user => ids.Contains(user.Id))
      .Select(user => new { user.Id, user.IdentityUserId }).ToListAsync(ct);
    var identityIds = users.Select(user => user.IdentityUserId).ToArray();
    var roles = await db.UserClaims.AsNoTracking().Where(claim => identityIds.Contains(claim.UserId) && claim.ClaimType == RoleClaim)
      .Select(claim => new { claim.UserId, claim.ClaimValue }).ToListAsync(ct);
    var byIdentity = roles.ToLookup(claim => claim.UserId, claim => claim.ClaimValue);
    return users.ToDictionary(user => user.Id, user => Resolve(byIdentity[user.IdentityUserId].ToArray()));
  }

  public async Task SetAsync(string identityId, string role, CancellationToken ct = default)
  {
    if (role is not ("Admin" or "Dispatch")) throw new ArgumentException("Unknown role.", nameof(role));
    await using var transaction = db.Database.CurrentTransaction is null ? await db.Database.BeginTransactionAsync(ct) : null;
    if (db.Database.IsNpgsql())
      await db.Database.ExecuteSqlInterpolatedAsync($"SELECT \"Id\" FROM \"AspNetUsers\" WHERE \"Id\" = {identityId} FOR UPDATE", ct);
    var claims = await db.UserClaims.Where(c => c.UserId == identityId && c.ClaimType == RoleClaim).OrderBy(c => c.Id).ToListAsync(ct);
    if (claims.Count == 0)
      db.UserClaims.Add(new IdentityUserClaim<string> { UserId = identityId, ClaimType = RoleClaim, ClaimValue = role });
    else
    {
      claims[0].ClaimValue = role;
      db.UserClaims.RemoveRange(claims.Skip(1));
    }
    await db.SaveChangesAsync(ct);
    if (transaction is not null) await transaction.CommitAsync(ct);
  }

  private static string Resolve(IReadOnlyCollection<string?> roles) =>
    roles.Count == 1 && roles.Single() == "Admin" ? "Admin" : "Dispatch";
}
