using API.Controllers;
using Application;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using InfrastructureServices = Infrastructure.DependencyInjection;

namespace Server.Tests.Architecture;

[Trait("Category", "Architecture")]
[Trait("Kind", "Architecture")]
public sealed class CompositionTests
{
  [Fact]
  public async Task LayerRegistrationsResolveTogetherWithoutCapturingScopedServices()
  {
    var builder = WebApplication.CreateBuilder();
    var services = builder.Services;
    services.AddRouting();
    services.AddDataProtection();
    services.AddApplication();
    InfrastructureServices.AddInfrastructure(services, builder.Configuration);
    await using var provider = services.BuildServiceProvider(
      new ServiceProviderOptions
      {
        ValidateOnBuild = true,
        ValidateScopes = true,
      }
    );
  }

  [Fact]
  public void PlanningControllersHaveNoBusinessOrInfrastructureDependencies()
  {
    foreach (
      var type in new[]
      {
        typeof(RoutePlanningController),
        typeof(SettingsController),
        typeof(SynchronizationController),
      }
    )
    {
      Assert.True(typeof(BaseController).IsAssignableFrom(type));
      Assert.All(
        type.GetConstructors(),
        constructor => Assert.Empty(constructor.GetParameters())
      );
    }
    Assert.DoesNotContain(
      typeof(DependencyInjection).Assembly.GetReferencedAssemblies(),
      assembly => assembly.Name is "Shared" or "Infrastructure" or "API"
    );
  }

  // A provider call that runs for the .NET default of 100 seconds can hold a
  // synchronization gate or a request for that long. Every provider client
  // states its own bound instead.
  [Fact]
  public async Task ProviderHttpClientsStateAnExplicitTimeout()
  {
    var builder = WebApplication.CreateBuilder();
    var services = builder.Services;
    services.AddRouting();
    services.AddDataProtection();
    services.AddApplication();
    InfrastructureServices.AddInfrastructure(services, builder.Configuration);
    await using var provider = services.BuildServiceProvider();
    var factory = provider.GetRequiredService<IHttpClientFactory>();

    foreach (
      var name in new[]
      {
        "SamsaraApiService",
        "IPlaceSearchService",
        "IWeatherProvider",
        "IIftaApiService",
        "IRoutingProvider",
        "IRouteAlternativesProvider",
        "IAddressGeocoder",
        "IAddressSuggestionsProvider",
        "IFuelExchangeRateProvider",
      }
    )
    {
      var timeout = factory.CreateClient(name).Timeout;
      Assert.True(
        timeout < TimeSpan.FromSeconds(100),
        $"{name} keeps the default HttpClient timeout."
      );
      Assert.True(timeout > TimeSpan.Zero, $"{name} has no usable timeout.");
    }
  }
}
