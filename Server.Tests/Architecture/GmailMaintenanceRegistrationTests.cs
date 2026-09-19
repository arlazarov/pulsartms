using Application.Features.Fuel.Interfaces;
using Application.Features.Synchronization.Interfaces;
using Infrastructure;
using Infrastructure.Integrations.Bvd;
using Infrastructure.Integrations.Google.Gmail;
using Infrastructure.Synchronization;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using static Server.Tests.Support.RepositoryFiles;

namespace Server.Tests.Architecture;

[Trait("Category", "Architecture")]
[Trait("Kind", "Architecture")]
public sealed class GmailMaintenanceRegistrationTests
{
  [Theory]
  [InlineData(null, true)]
  [InlineData("true", true)]
  [InlineData("false", false)]
  public void MaintenanceSettingControlsOnlyTheGmailHostedWorker(
    string? configured,
    bool enabled
  )
  {
    var configuration = new ConfigurationBuilder()
      .AddInMemoryCollection(
        new Dictionary<string, string?>
        {
          ["Gmail:BackgroundMaintenanceEnabled"] = configured,
        }
      )
      .Build();
    var services = new ServiceCollection().AddInfrastructure(configuration);
    var worker = typeof(ApplicationWorker<IGmailWatchOperation>);
    Assert.Equal(
      enabled ? 1 : 0,
      services.Count(x =>
        x.ServiceType == typeof(IHostedService)
        && x.ImplementationType == worker
      )
    );
    Assert.Contains(
      services,
      x => x.ServiceType == typeof(GmailServiceFactory)
    );
    Assert.Contains(
      services,
      x => x.ServiceType == typeof(GmailAttachmentService)
    );
    Assert.Contains(
      services,
      x =>
        x.ServiceType == typeof(IGmailWatchService)
        && x.ImplementationType == typeof(GmailWatchService)
    );
    Assert.Contains(
      services,
      x =>
        x.ServiceType == typeof(IGmailWatchStore)
        && x.ImplementationType == typeof(GmailWatchStore)
    );
    Assert.Contains(
      services,
      x =>
        x.ServiceType == typeof(IGmailPushValidator)
        && x.ImplementationType == typeof(GmailPushValidator)
    );
    Assert.Contains(
      services,
      x =>
        x.ServiceType == typeof(IFuelDiscountProvider)
        && x.ImplementationType == typeof(BvdFuelDiscountProvider)
    );

    var defaults = new ServiceCollection().AddInfrastructure(
      new ConfigurationBuilder().Build()
    );
    Assert.Equal(
      defaults.Where(x => x.ImplementationType != worker).Select(Descriptor),
      services.Where(x => x.ImplementationType != worker).Select(Descriptor)
    );
  }

  [Fact]
  public void InvalidMaintenanceSettingDoesNotSilentlyDisableTheWorker()
  {
    var configuration = new ConfigurationBuilder()
      .AddInMemoryCollection(
        new Dictionary<string, string?>
        {
          ["Gmail:BackgroundMaintenanceEnabled"] = "invalid",
        }
      )
      .Build();
    Assert.Throws<InvalidOperationException>(
      () => new ServiceCollection().AddInfrastructure(configuration)
    );
  }

  [Fact]
  public void DevelopmentDisablesAutomaticMaintenanceExplicitly()
  {
    var configuration = new ConfigurationBuilder()
      .AddJsonFile(
        Path.Combine(Root(), "Server/API/appsettings.Development.json")
      )
      .Build();
    Assert.False(
      configuration.GetValue("Gmail:BackgroundMaintenanceEnabled", true)
    );
  }

  private static (Type, ServiceLifetime, Type?) Descriptor(
    ServiceDescriptor value
  ) => (value.ServiceType, value.Lifetime, value.ImplementationType);
}
