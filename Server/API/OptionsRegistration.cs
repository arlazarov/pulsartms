using Application.Diagnostics.Consistency;
using Application.Features.Dispatch.Options;
using Application.Features.Fuel.Options;
using Application.Features.Routing.Options;
using Application.Features.Synchronization.Options;
using Application.Storage;
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
    Bind<ConsistencyAuditOptions>("ConsistencyAudit");
    Bind<StorageOptions>("Storage");
    Bind<MessagingOptions>("Messaging");
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
