using System.Security.Claims;
using Application.Interfaces;
using Microsoft.AspNetCore.Authorization;

namespace Infrastructure.Identity;

// A deployment operator (IDeploymentOperators), whatever roles the user
// holds within a carrier.
public sealed class OperatorRequirement : IAuthorizationRequirement;

public sealed class OperatorAuthorizationHandler(IDeploymentOperators operators)
  : AuthorizationHandler<OperatorRequirement>
{
  protected override Task HandleRequirementAsync(
    AuthorizationHandlerContext context,
    OperatorRequirement requirement
  )
  {
    if (
      context.User.Identity?.IsAuthenticated == true
      && operators.Includes(
        context.User.FindFirstValue(ClaimTypes.NameIdentifier)
      )
    )
      context.Succeed(requirement);
    return Task.CompletedTask;
  }
}
