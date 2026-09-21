using Application.Features.Integrations.Models;
using Infrastructure.Integrations;
using Microsoft.Extensions.Configuration;

namespace Server.Tests.Identity;

[Trait("Category", "Identity")]
[Trait("Kind", "Unit")]
public sealed class CompanyDeploymentCredentialTests
{
  [Fact]
  public void ANewCarrierDoesNotInheritTheOriginalCarriersDeploymentSecret()
  {
    var company = new TestCompany();
    var configuration = new ConfigurationBuilder()
      .AddInMemoryCollection(
        new Dictionary<string, string?> { ["TorqueAI:ApiKey"] = "original" }
      )
      .Build();
    var credentials = new IntegrationDeploymentCredentials(
      configuration,
      company
    );
    Assert.Equal(
      "original",
      credentials.Get(IntegrationProviderCatalog.Torque).Get("apiKey")
    );
    using (company.As(Guid.NewGuid()))
      Assert.Empty(
        credentials.Get(IntegrationProviderCatalog.Torque).FieldNames
      );
  }
}
