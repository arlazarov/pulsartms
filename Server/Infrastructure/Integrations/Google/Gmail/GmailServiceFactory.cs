using Application.Features.Integrations.Interfaces;
using Application.Features.Integrations.Models;
using Google.Apis.Auth.OAuth2;
using Google.Apis.Auth.OAuth2.Flows;
using Google.Apis.Auth.OAuth2.Responses;
using Google.Apis.Gmail.v1;
using Google.Apis.Services;

namespace Infrastructure.Integrations.Google.Gmail;

public class GmailServiceFactory(IIntegrationCredentials credentials)
{
  public async Task<GmailService> CreateAsync(CancellationToken cancellationToken = default)
  {
    cancellationToken.ThrowIfCancellationRequested();
    var values = await credentials.GetAsync(IntegrationProviderCatalog.GoogleEmail, cancellationToken);
    cancellationToken.ThrowIfCancellationRequested();
    var credential = CreateCredential(values);
    return new GmailService(
      new BaseClientService.Initializer
      {
        HttpClientInitializer = credential,
        ApplicationName = "AMFTMS",
      }
    );
  }

  private static ICredential CreateCredential(IntegrationCredentialValues values)
  {
    var clientId = Required(values, "clientId");
    var clientSecret = Required(values, "clientSecret");
    var refreshToken = Required(values, "refreshToken");

    var flow = new GoogleAuthorizationCodeFlow(
      new GoogleAuthorizationCodeFlow.Initializer
      {
        ClientSecrets = new ClientSecrets { ClientId = clientId, ClientSecret = clientSecret },
        Scopes = [GmailService.Scope.GmailReadonly],
      }
    );

    return new UserCredential(flow, "amftms", new TokenResponse { RefreshToken = refreshToken });
  }

  private static string Required(IntegrationCredentialValues values, string field) => !string.IsNullOrWhiteSpace(values.Get(field))
    ? values.Get(field)! : throw new InvalidOperationException($"Gmail {field} is not configured.");
}
