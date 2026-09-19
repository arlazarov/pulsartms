using Application.Features.Integrations.Interfaces;
using Application.Features.Integrations.Models;
using Google.Apis.Auth.OAuth2;
using Google.Apis.Auth.OAuth2.Flows;
using Google.Apis.Gmail.v1;
using Infrastructure.Integrations.Google.Gmail;

namespace Server.Tests.Identity;

[Trait("Category", "Identity")]
[Trait("Kind", "Unit")]
public class GmailCredentialTests
{
  [Fact]
  public async Task EachClientUsesOneCompleteRotatableReadonlyCredentialSnapshot()
  {
    var resolver = new StubProviderCredentials { Values = Values("first") };
    var factory = new GmailServiceFactory(resolver);
    using var first = await factory.CreateAsync();
    resolver.Values = Values("second");
    using var second = await factory.CreateAsync();

    AssertCredential(first, "first");
    AssertCredential(second, "second");
    Assert.Equal(
      [
        IntegrationProviderCatalog.GoogleEmail,
        IntegrationProviderCatalog.GoogleEmail,
      ],
      resolver.Requests
    );
  }

  [Theory]
  [InlineData("clientId")]
  [InlineData("clientSecret")]
  [InlineData("refreshToken")]
  public async Task MissingFieldErrorsNeverDiscloseOtherCredentialValues(
    string missing
  )
  {
    var fields = new[] { "clientId", "clientSecret", "refreshToken" };
    var resolver = new StubProviderCredentials(
      fields
        .Where(field => field != missing)
        .Select(field => (field, $"sensitive-{field}"))
        .ToArray()
    );
    var factory = new GmailServiceFactory(resolver);

    var error = await Assert.ThrowsAsync<InvalidOperationException>(
      () => factory.CreateAsync()
    );

    Assert.Contains(missing, error.Message);
    foreach (var field in fields)
      Assert.DoesNotContain($"sensitive-{field}", error.ToString());
    Assert.Equal(
      IntegrationProviderCatalog.GoogleEmail,
      Assert.Single(resolver.Requests)
    );
  }

  [Fact]
  public async Task CancelledCreationDoesNotResolveOrStartAuthorization()
  {
    var resolver = new StubProviderCredentials { Values = Values("unused") };
    using var cancellation = new CancellationTokenSource();
    cancellation.Cancel();
    await Assert.ThrowsAnyAsync<OperationCanceledException>(
      () => new GmailServiceFactory(resolver).CreateAsync(cancellation.Token)
    );
    Assert.Empty(resolver.Requests);
  }

  [Fact]
  public async Task ResolverFailurePreventsClientCreation()
  {
    var error = await Assert.ThrowsAsync<InvalidOperationException>(
      () => new GmailServiceFactory(new FailingCredentials()).CreateAsync()
    );
    Assert.Equal("Credentials unavailable.", error.Message);
  }

  private static IntegrationCredentialValues Values(string prefix) =>
    new(
      new[] { "clientId", "clientSecret", "refreshToken" }.Select(field =>
        KeyValuePair.Create(field, $"{prefix}-{field}")
      )
    );

  private static void AssertCredential(GmailService service, string prefix)
  {
    var credential = Assert.IsType<UserCredential>(
      service.HttpClientInitializer
    );
    var flow = Assert.IsType<GoogleAuthorizationCodeFlow>(credential.Flow);
    Assert.Equal($"{prefix}-clientId", flow.ClientSecrets.ClientId);
    Assert.Equal($"{prefix}-clientSecret", flow.ClientSecrets.ClientSecret);
    Assert.Equal($"{prefix}-refreshToken", credential.Token.RefreshToken);
    Assert.Equal(GmailService.Scope.GmailReadonly, Assert.Single(flow.Scopes));
    Assert.Null(flow.DataStore);
    Assert.Null(credential.Token.AccessToken);
    Assert.Equal("pulsartms", credential.UserId);
    Assert.Equal("PulsR", service.ApplicationName);
  }

  private sealed class FailingCredentials : IIntegrationCredentials
  {
    public Task<IntegrationCredentialValues> GetAsync(
      string provider,
      CancellationToken ct
    ) =>
      Task.FromException<IntegrationCredentialValues>(
        new InvalidOperationException("Credentials unavailable.")
      );
  }
}
