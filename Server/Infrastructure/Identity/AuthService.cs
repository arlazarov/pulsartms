using Application.Features.Auth.Interfaces;
using Application.Interfaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.BearerToken;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;

namespace Infrastructure.Identity;

public class AuthService(
  UserManager<AppUser> userManager,
  SignInManager<AppUser> signInManager,
  IHttpContextAccessor httpContextAccessor,
  IOptionsMonitor<BearerTokenOptions> bearerTokenOptions,
  TimeProvider timeProvider,
  IAppDbContext dbContext, IReadCache? reads = null
) : IAuthService
{
  public async Task<bool> LoginAsync(
    string email,
    string password,
    CancellationToken cancellationToken = default
  )
  {
    var user = await userManager.FindByEmailAsync(email);

    if (user is null || !await dbContext.Users.AnyAsync(
      x => x.IdentityUserId == user.Id && x.IsActive, cancellationToken))
      return false;

    var result = await signInManager.CheckPasswordSignInAsync(
      user, password, lockoutOnFailure: true
    );

    if (!result.Succeeded)
    {
      return false;
    }

    var principal = await signInManager.CreateUserPrincipalAsync(user);

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

    var user = await signInManager.ValidateSecurityStampAsync(refreshTicket.Principal);

    if (user is null || !await dbContext.Users.AnyAsync(
      x => x.IdentityUserId == user.Id && x.IsActive, cancellationToken))
      return false;

    var principal = await signInManager.CreateUserPrincipalAsync(user);

    var httpContext = httpContextAccessor.HttpContext;

    if (httpContext is null)
      return false;

    await httpContext.SignInAsync(IdentityConstants.BearerScheme, principal);

    return true;
  }

  public async Task LogoutAsync()
  {
    var principal = httpContextAccessor.HttpContext?.User
      ?? throw new InvalidOperationException("An authenticated request is required.");
    var user = await userManager.GetUserAsync(principal)
      ?? throw new InvalidOperationException("The user no longer exists.");
    var result = await userManager.UpdateSecurityStampAsync(user);
    if (!result.Succeeded)
      throw new InvalidOperationException("Could not revoke user sessions.");
    reads?.Invalidate($"session:{user.Id}");
  }
}
