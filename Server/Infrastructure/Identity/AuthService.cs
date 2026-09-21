using Application.Features.Auth.Interfaces;
using Application.Interfaces;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.BearerToken;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Infrastructure.Identity;

public class AuthService(
  UserManager<AppUser> userManager,
  SignInManager<AppUser> signInManager,
  IHttpContextAccessor httpContextAccessor,
  IOptionsMonitor<BearerTokenOptions> bearerTokenOptions,
  TimeProvider timeProvider,
  IAppDbContext dbContext,
  IReadCache? reads = null
) : IAuthService
{
  public async Task<bool> LoginAsync(
    string email,
    string password,
    CancellationToken cancellationToken = default
  )
  {
    var user = await userManager.FindByEmailAsync(email);

    // Signing in is the one read that cannot be narrowed to a carrier:
    // it is the read that discovers which carrier this is. An address
    // identifies a person across the whole system, and that person's
    // company is what every later query is then narrowed by.
    var account = user is null
      ? null
      : await dbContext
        .Users.IgnoreQueryFilters()
        .Where(x => x.IdentityUserId == user.Id && x.IsActive)
        .Select(x => new { x.CompanyId })
        .SingleOrDefaultAsync(cancellationToken);
    if (user is null || account is null)
      return false;

    var result = await signInManager.CheckPasswordSignInAsync(
      user,
      password,
      lockoutOnFailure: true
    );

    if (!result.Succeeded)
    {
      return false;
    }

    var principal = await signInManager.CreateUserPrincipalAsync(user);
    Carry(principal, account.CompanyId);

    var httpContext = httpContextAccessor.HttpContext;

    if (httpContext is null)
    {
      return false;
    }

    await httpContext.SignInAsync(IdentityConstants.BearerScheme, principal);

    return true;
  }

  public async Task<bool> RefreshAsync(
    string refreshToken,
    CancellationToken cancellationToken = default
  )
  {
    var options = bearerTokenOptions.Get(IdentityConstants.BearerScheme);

    var refreshTicket = options.RefreshTokenProtector.Unprotect(refreshToken);

    if (refreshTicket?.Properties.ExpiresUtc is not { } expiresUtc)
      return false;

    if (timeProvider.GetUtcNow() >= expiresUtc)
      return false;

    var user = await signInManager.ValidateSecurityStampAsync(
      refreshTicket.Principal
    );

    var account = user is null
      ? null
      : await dbContext
        .Users.IgnoreQueryFilters()
        .Where(x => x.IdentityUserId == user.Id && x.IsActive)
        .Select(x => new { x.CompanyId })
        .SingleOrDefaultAsync(cancellationToken);
    if (user is null || account is null)
      return false;

    var principal = await signInManager.CreateUserPrincipalAsync(user);
    Carry(principal, account.CompanyId);

    var httpContext = httpContextAccessor.HttpContext;

    if (httpContext is null)
      return false;

    await httpContext.SignInAsync(IdentityConstants.BearerScheme, principal);

    return true;
  }

  // The carrier travels inside the token, so it is decided here and never
  // taken from anything the caller sends.
  private static void Carry(
    System.Security.Claims.ClaimsPrincipal principal,
    Guid company
  )
  {
    var identity = principal.Identities.First();
    foreach (var stale in identity.FindAll(CurrentCompany.Claim).ToArray())
      identity.RemoveClaim(stale);
    identity.AddClaim(new(CurrentCompany.Claim, company.ToString()));
  }

  public async Task LogoutAsync()
  {
    var principal =
      httpContextAccessor.HttpContext?.User
      ?? throw new InvalidOperationException(
        "An authenticated request is required."
      );
    var user =
      await userManager.GetUserAsync(principal)
      ?? throw new InvalidOperationException("The user no longer exists.");
    var result = await userManager.UpdateSecurityStampAsync(user);
    if (!result.Succeeded)
      throw new InvalidOperationException("Could not revoke user sessions.");
    reads?.Invalidate($"session:{user.Id}");
  }
}
