using System.Text.RegularExpressions;
using Application.Interfaces;
using Microsoft.Extensions.Configuration;

namespace Infrastructure.Diagnostics;

// Cloud Run names each revision in K_REVISION: lowercase letters, digits
// and hyphens, starting with a letter, at most 63 characters. Anything
// else, or nothing, is no name - never a default another process shares.
public sealed partial class DeploymentRevision(IConfiguration configuration)
  : IDeploymentRevision
{
  public string? Name { get; } =
    configuration["K_REVISION"] is { } name && Valid().IsMatch(name)
      ? name
      : null;

  [GeneratedRegex("^[a-z](?:[-a-z0-9]{0,61}[a-z0-9])?$")]
  private static partial Regex Valid();
}
