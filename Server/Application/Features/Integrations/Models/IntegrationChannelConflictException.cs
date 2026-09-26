namespace Application.Features.Integrations.Models;

public sealed class IntegrationChannelConflictException : Exception
{
  public IntegrationChannelConflictException()
    : base("The channel is already held by another company.") { }
}
