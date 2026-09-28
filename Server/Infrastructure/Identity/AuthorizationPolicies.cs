using Microsoft.AspNetCore.Authorization;

namespace Infrastructure.Identity;

// The named policies endpoints ask for, and the fallback: any signed-in
// user.
public static class AuthorizationPolicies
{
  public static void Configure(AuthorizationOptions options)
  {
    options.AddPolicy(
      "Admin",
      policy =>
        policy
          .RequireAuthenticatedUser()
          .AddRequirements(new AdminRequirement())
    );
    // Deployment operators: what they do reaches every carrier, so a
    // carrier's Admin role is required and is not enough.
    options.AddPolicy(
      "Operator",
      policy =>
        policy
          .RequireAuthenticatedUser()
          .AddRequirements(new AdminRequirement(), new OperatorRequirement())
    );
    options.AddPolicy(
      "Dispatch",
      policy =>
        policy
          .RequireAuthenticatedUser()
          .AddRequirements(new DispatchRequirement())
    );
    options.FallbackPolicy = new AuthorizationPolicyBuilder()
      .RequireAuthenticatedUser()
      .Build();
  }
}
