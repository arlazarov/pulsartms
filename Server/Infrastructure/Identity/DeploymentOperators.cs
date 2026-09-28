using Application.Interfaces;
using Microsoft.Extensions.Configuration;

namespace Infrastructure.Identity;

// Operations:Operators lists the identity ids of the people who operate
// this deployment; none listed, nobody is one.
public sealed class DeploymentOperators(IConfiguration configuration)
  : IDeploymentOperators
{
  private readonly HashSet<string> operators =
  [
    .. (
      configuration.GetSection("Operations:Operators").Get<string[]>() ?? []
    ).Where(x => !string.IsNullOrWhiteSpace(x)),
  ];

  public bool Includes(string? identityUserId) =>
    identityUserId is { Length: > 0 } && operators.Contains(identityUserId);
}
