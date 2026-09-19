using Application.Features.Dispatch.Options;
using Application.Features.Eta.Options;
using Application.Features.Routing.Options;
using Application.Features.Synchronization.Options;

namespace API;

public static class OptionsRegistration
{
  public static IServiceCollection AddApplicationOptions(
    this IServiceCollection services,
    IConfiguration configuration
  )
  {
    Bind<SynchronizationOptions>("Synchronization");
    Bind<DispatchImportOptions>("DispatchImport");
    Bind<FuelRegionOptions>("FuelRegions");
    Bind<RouteRecalculationBudgetOptions>("RouteRecalculationBudget");
    Bind<RoutePreparationOptions>("RoutePreparation");
    Bind<EtaPlanningOptions>("EtaPlanning");
    return services;

    void Bind<T>(string section)
      where T : class =>
      services
        .AddOptions<T>()
        .Bind(configuration.GetSection(section))
        .ValidateDataAnnotations()
        .ValidateOnStart();
  }
}
