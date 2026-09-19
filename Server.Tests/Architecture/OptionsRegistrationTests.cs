using API;
using Application.Features.Eta.Options;
using Application.Features.Routing.Options;
using Application.Features.Synchronization.Options;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Server.Tests.Architecture;

[Trait("Category", "Architecture")]
[Trait("Kind", "Architecture")]
public sealed class OptionsRegistrationTests
{
  [Fact]
  public void CompositionBindsNamedFeatureSectionsAndValidatesWithoutDatabaseOrProviderServices()
  {
    var configuration = new ConfigurationBuilder()
      .AddInMemoryCollection(
        new Dictionary<string, string?>
        {
          ["Synchronization:TelemetrySeconds"] = "30",
          ["FuelRegions:CellMiles"] = "60",
          ["RouteRecalculationBudget:Enabled"] = "false",
          ["EtaPlanning:PlanningSpeedCapMph"] = "55",
          ["RoutePreparation:BatchSize"] = "5",
        }
      )
      .Build();
    using var provider = new ServiceCollection()
      .AddApplicationOptions(configuration)
      .BuildServiceProvider();
    provider.GetRequiredService<IStartupValidator>().Validate();
    Assert.Equal(
      30,
      provider
        .GetRequiredService<IOptions<SynchronizationOptions>>()
        .Value.TelemetrySeconds
    );
    Assert.Equal(
      60,
      provider.GetRequiredService<IOptions<FuelRegionOptions>>().Value.CellMiles
    );
    Assert.False(
      provider
        .GetRequiredService<IOptions<RouteRecalculationBudgetOptions>>()
        .Value.Enabled
    );
    Assert.Equal(
      55,
      provider
        .GetRequiredService<IOptions<EtaPlanningOptions>>()
        .Value.PlanningSpeedCapMph
    );
    Assert.Equal(
      5,
      provider
        .GetRequiredService<IOptions<RoutePreparationOptions>>()
        .Value.BatchSize
    );
  }

  [Theory]
  [InlineData("Synchronization:TelemetrySeconds", "0")]
  [InlineData("FuelRegions:CellMiles", "0")]
  [InlineData("EtaPlanning:PlanningSpeedCapMph", "0")]
  [InlineData("RoutePreparation:BatchSize", "0")]
  public void InvalidValuesFailAtStartup(string key, string value)
  {
    var configuration = new ConfigurationBuilder()
      .AddInMemoryCollection(new Dictionary<string, string?> { [key] = value })
      .Build();
    using var provider = new ServiceCollection()
      .AddApplicationOptions(configuration)
      .BuildServiceProvider();
    Assert.Throws<OptionsValidationException>(
      () => provider.GetRequiredService<IStartupValidator>().Validate()
    );
  }
}
