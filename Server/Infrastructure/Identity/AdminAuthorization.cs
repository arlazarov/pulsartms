using System.Security.Claims;
using Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Configuration;

namespace Infrastructure.Identity;

public sealed class AdminRequirement : IAuthorizationRequirement;

public sealed class AdminAuthorizationHandler(IUserRoleService roles)
  : AuthorizationHandler<AdminRequirement>
{
  protected override async Task HandleRequirementAsync(
    AuthorizationHandlerContext context,
    AdminRequirement requirement
  )
  {
    var id = context.User.FindFirstValue(ClaimTypes.NameIdentifier);
    if (
      context.User.Identity?.IsAuthenticated == true
      && id is not null
      && await roles.GetAsync(id) == "Admin"
    )
      context.Succeed(requirement);
  }
}
