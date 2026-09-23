using Application.Features.Dispatch.Options;
using Application.Features.Fuel.Options;
using Application.Features.Synchronization.Options;
using Domain.Policies;

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
    Bind<FuelStationStatusOptions>("FuelStationStatus");
    Bind<RouteRecalculationBudgetOptions>("RouteRecalculationBudget");
    Bind<RoutePreparationOptions>("RoutePreparation");
    Bind<EtaPlanningOptions>("EtaPlanning");
    Bind<FuelIssueOptions>("FuelIssue");
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
