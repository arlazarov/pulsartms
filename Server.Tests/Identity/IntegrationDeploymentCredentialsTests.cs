using Application.Features.Integrations.Models;
using Infrastructure.Integrations;
using Microsoft.Extensions.Configuration;

namespace Server.Tests.Identity;

[Trait("Category", "Identity")]
[Trait("Kind", "Unit")]
public sealed class IntegrationDeploymentCredentialsTests
{
  [Fact]
  public void ReadsOnlyTheExistingKeysForTheThreeApprovedProviders()
  {
    var configuration = new ConfigurationBuilder()
      .AddInMemoryCollection(
        new Dictionary<string, string?>
        {
          ["TorqueAI:ApiKey"] = "fixture-torque",
          ["Samsara:ApiToken"] = "fixture-samsara",
          ["Gmail:ClientId"] = "fixture-client",
          ["Gmail:ClientSecret"] = "fixture-secret",
          ["Gmail:RefreshToken"] = "fixture-refresh",
          ["GoogleMaps:ApiKey"] = "fixture-browser",
          ["GooglePlaces:ApiKey"] = "fixture-places",
          ["TomTom:ApiKey"] = "fixture-route",
        }
      )
      .Build();
    var adapter = new IntegrationDeploymentCredentials(configuration);
    Assert.Equal(
      "fixture-torque",
      adapter.Get(IntegrationProviderCatalog.Torque).Get("apiKey")
    );
    Assert.Equal(
      "fixture-samsara",
      adapter.Get(IntegrationProviderCatalog.Samsara).Get("apiKey")
    );
    var gmail = adapter.Get(IntegrationProviderCatalog.GoogleEmail);
    Assert.Equal("fixture-client", gmail.Get("clientId"));
    Assert.Equal("fixture-secret", gmail.Get("clientSecret"));
    Assert.Equal("fixture-refresh", gmail.Get("refreshToken"));
    Assert.Equal(3, gmail.FieldNames.Count);
    Assert.Throws<ArgumentException>(() => adapter.Get("google-maps"));
    Assert.Throws<ArgumentException>(() => adapter.Get("tomtom"));
    Assert.Equal("[Integration credentials]", gmail.ToString());
  }

  [Fact]
  public void MissingDeploymentFieldsRemainUnconfiguredWithoutInventingValues()
  {
    var adapter = new IntegrationDeploymentCredentials(
      new ConfigurationBuilder().Build()
    );
    foreach (var provider in IntegrationProviderCatalog.Providers)
    {
      var values = adapter.Get(provider);
      Assert.Empty(values.FieldNames);
      Assert.False(IntegrationProviderCatalog.IsConfigured(provider, values));
    }
  }

  [Fact]
  public void DeploymentValuesArePreservedExactlyAndNotCapturedAtConstruction()
  {
    var configuration = new ConfigurationBuilder()
      .AddInMemoryCollection(
        new Dictionary<string, string?>
        {
          ["Samsara:ApiToken"] = " fixture-original ",
        }
      )
      .Build();
    var adapter = new IntegrationDeploymentCredentials(configuration);
    Assert.Equal(
      " fixture-original ",
      adapter.Get(IntegrationProviderCatalog.Samsara).Get("apiKey")
    );
    configuration["Samsara:ApiToken"] = "fixture-replacement";
    Assert.Equal(
      "fixture-replacement",
      adapter.Get(IntegrationProviderCatalog.Samsara).Get("apiKey")
    );
  }
}
