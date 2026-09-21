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
        // Who someone is, and whether their account is still live, is
        // asked before their carrier is known - it is the question that
        // decides the carrier. Like signing in, it is not narrowed by one.
        && await dbContext
          .Users.IgnoreQueryFilters()
          .AnyAsync(
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
      // A token issued before carriers existed names nobody's carrier. It
      // is still a good token, but every query it made would be narrowed
      // to no carrier at all and the person would be looking at an empty
      // product. Answering 401 sends the browser down the refresh path it
      // already has, and the token that comes back carries the carrier.
      if (valid && context.User.FindFirstValue(CurrentCompany.Claim) is null)
        valid = false;
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
