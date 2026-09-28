using Application.Interfaces;
using Microsoft.Extensions.Configuration;

namespace Infrastructure.Diagnostics;

// Cloud Run names each revision in K_REVISION; elsewhere the process is
// "local".
public sealed class DeploymentRevision(IConfiguration configuration)
  : IDeploymentRevision
{
  public string Name { get; } =
    configuration["K_REVISION"] is { Length: > 0 and <= 128 } name
      ? name
      : "local";
}
