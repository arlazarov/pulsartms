using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Application.Features.Synchronization.Options;
using Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Infrastructure.Identity;

public class SessionValidationMiddleware(RequestDelegate next)
{
  public async Task InvokeAsync(
    HttpContext context,
    SignInManager<AppUser> signInManager,
    IAppDbContext dbContext,
    IReadCache? reads = null,
    IOptions<SynchronizationOptions>? options = null
  )
  {
    if (
      context.User.Identity?.IsAuthenticated == true
      && context.GetEndpoint()?.Metadata.GetMetadata<IAllowAnonymous>() is null
    )
    {
      var id = context.User.FindFirstValue(ClaimTypes.NameIdentifier);
      var stamp =
        context.User.FindFirstValue(
          signInManager.Options.ClaimsIdentity.SecurityStampClaimType
        ) ?? "";
      async Task<bool> ValidAsync() =>
        await signInManager.ValidateSecurityStampAsync(context.User) is not null
        && await dbContext.Users.AnyAsync(
          x => x.IdentityUserId == id && x.IsActive,
          context.RequestAborted
        );
      var valid = reads is null
        ? await ValidAsync()
        : await reads.GetAsync(
          $"session:{id}",
          Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(stamp))),
          ValidAsync,
          TimeSpan.FromSeconds(options?.Value.SessionValidationSeconds ?? 30)
        );
      if (!valid)
      {
        context.Response.StatusCode = StatusCodes.Status401Unauthorized;
        context.Response.Headers.WWWAuthenticate = "Bearer";
        return;
      }
    }

    await next(context);
  }
}
