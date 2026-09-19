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
}
