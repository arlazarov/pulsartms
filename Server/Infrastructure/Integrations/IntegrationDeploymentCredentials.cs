using Application.Features.Integrations.Interfaces;
using Application.Features.Integrations.Models;
using Application.Interfaces;
using Domain.Entities;
using Microsoft.Extensions.Configuration;

namespace Infrastructure.Integrations;

public sealed class IntegrationDeploymentCredentials(
  IConfiguration configuration,
  ICurrentCompany companies
) : IIntegrationDeploymentCredentials
{
  public IntegrationCredentialValues Get(string provider)
  {
    // WhatsApp has no server configuration: a carrier's messaging number is
    // only what its administrator saved in Settings.
    if (
      companies.Id != Company.Amf
      || provider == IntegrationProviderCatalog.WhatsApp
    )
      return new([]);
    var keys = provider switch
    {
      IntegrationProviderCatalog.Torque => new[]
      {
        ("apiKey", "TorqueAI:ApiKey"),
      },
      IntegrationProviderCatalog.Samsara => new[]
      {
        ("apiKey", "Samsara:ApiToken"),
      },
      IntegrationProviderCatalog.GoogleEmail => new[]
      {
        ("clientId", "Gmail:ClientId"),
        ("clientSecret", "Gmail:ClientSecret"),
        ("refreshToken", "Gmail:RefreshToken"),
      },
      _ => throw new ArgumentException(
        "Unsupported integration.",
        nameof(provider)
      ),
    };
    return new(
      keys.Select(key => new KeyValuePair<string, string?>(
          key.Item1,
          configuration[key.Item2]
        ))
        .Where(value => value.Value is not null)
        .Select(value => new KeyValuePair<string, string>(
          value.Key,
          value.Value!
        ))
    );
  }
}
