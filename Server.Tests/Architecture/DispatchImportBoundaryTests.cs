using Application.Features.Dispatch.Interfaces;
using Application.Features.Fleet.Interfaces;
using Infrastructure;
using Infrastructure.Integrations.Torque;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using static Server.Tests.Support.RepositoryFiles;

namespace Server.Tests.Architecture;

[Trait("Category", "Architecture")]
[Trait("Kind", "Architecture")]
public sealed class DispatchImportBoundaryTests
{
  [Theory]
  [InlineData(null, false)]
  [InlineData("", false)]
  [InlineData("torqueai", true)]
  [InlineData(" TorqueAI ", true)]
  public void AdapterIsRegisteredOnlyForExplicitProvider(
    string? key,
    bool enabled
  )
  {
    var configuration = new ConfigurationBuilder()
      .AddInMemoryCollection(
        new Dictionary<string, string?> { ["DispatchImport:Provider"] = key }
      )
      .Build();
    var services = new ServiceCollection().AddInfrastructure(configuration);
    Assert.Equal(
      enabled,
      services.Any(x => x.ServiceType == typeof(IDispatchProvider))
    );
    Assert.Equal(
      enabled,
      services.Any(x => x.ServiceType == typeof(TorqueApiService))
    );
    Assert.Contains(
      services,
      x => x.ServiceType == typeof(IFleetTelemetryFeedProvider)
    );
  }

  [Fact]
  public void OperationalCoreContainsNoTorqueTypesOrNames()
  {
    var root = Root();
    foreach (var layer in new[] { "Application", "Domain" })
    foreach (var file in Sources(Path.Combine(root, "Server", layer), "*.cs"))
    {
      if (file.Contains("/Features/Integrations/"))
        continue;
      Assert.False(
        File.ReadAllText(file)
          .Contains("Torque", StringComparison.OrdinalIgnoreCase),
        file
      );
    }
  }

  [Fact]
  public void UnsupportedProviderFailsAtComposition()
  {
    var configuration = new ConfigurationBuilder()
      .AddInMemoryCollection(
        new Dictionary<string, string?>
        {
          ["DispatchImport:Provider"] = "unknown",
        }
      )
      .Build();
    Assert.Throws<InvalidOperationException>(
      () => new ServiceCollection().AddInfrastructure(configuration)
    );
  }
}
