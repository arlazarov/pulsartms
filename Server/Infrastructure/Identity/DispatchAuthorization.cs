using System.Security.Claims;
using Application.Interfaces;
using Microsoft.AspNetCore.Authorization;

namespace Infrastructure.Identity;

public sealed class DispatchRequirement : IAuthorizationRequirement;

public sealed class DispatchAuthorizationHandler(IUserRoleService roles)
  : AuthorizationHandler<DispatchRequirement>
{
  protected override async Task HandleRequirementAsync(
    AuthorizationHandlerContext context,
    DispatchRequirement requirement
  )
  {
    var id = context.User.FindFirstValue(ClaimTypes.NameIdentifier);
    if (
      context.User.Identity?.IsAuthenticated == true
      && !string.IsNullOrWhiteSpace(id)
      && await roles.GetAsync(id) is "Admin" or "Dispatch"
    )
      context.Succeed(requirement);
  }
}
