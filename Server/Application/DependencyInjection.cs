using Application.Features.Routing.Services.Routes;
using Application.Features.Routing.Services.Addresses;
using Application.Features.Routing.Services.Deadheads;
using Application.Features.Routing.Services.FuelPlanning;
using Application.Caching;
using Application.Features.Fleet.Services;
using Application.Features.Routing.Background;
using Application.Features.Eta.Background;
using Application.Features.Fleet.Background;
using Application.Features.Synchronization.Services;
using Application.Features.Routing.Services;
using Application.Features.Fleet.Queries.GetFleetLocations;
using Application.Behaviors;
using Microsoft.Extensions.DependencyInjection;

namespace Application;

public static class DependencyInjection
{
  public static IServiceCollection AddApplication(
    this IServiceCollection services,
    string? mediatrLicenseKey = null
  )
  {
    services.AddMediatR(cfg =>
    {
      cfg.RegisterServicesFromAssembly(typeof(DependencyInjection).Assembly);
      cfg.AddOpenBehavior(typeof(RequestDiagnosticsBehavior<,>));
      cfg.AddOpenBehavior(typeof(AdminAuditBehavior<,>));
      cfg.AddOpenBehavior(typeof(PlanningExceptionBehavior<,>));
      cfg.AddOpenBehavior(typeof(ValidationBehavior<,>));
      cfg.LicenseKey = mediatrLicenseKey;
    });

    services.AddValidatorsFromAssembly(typeof(DependencyInjection).Assembly);

    services.AddScoped<FleetCache>();
    services.AddScoped<Features.Dispatch.Interfaces.IDispatchBoardReader, Features.Dispatch.Services.DispatchBoardReader>();
    services.AddScoped<Features.Dispatch.Services.DispatchBoardService>();
    services.AddScoped<Features.Integrations.Services.IntegrationSettingsService>();
    services.AddScoped<Features.Integrations.Interfaces.IIntegrationCredentials>(sp =>
      sp.GetRequiredService<Features.Integrations.Services.IntegrationSettingsService>());
    services.AddSingleton<FleetTelemetryCache>();
    services.AddSingleton<FleetLocationStream>();
    services.AddMemoryCache();
    services.AddSingleton(TimeProvider.System);
    services.AddScoped<Features.Fuel.Services.GmailWatchLifecycle>();
    services.AddScoped<Features.Fuel.Services.FuelStationLookupService>();
    services.AddSingleton<Features.Synchronization.Interfaces.IGmailWatchOperation, Features.Fuel.Background.GmailWatchOperation>();
    services.AddSingleton<ReadCache>();
    services.AddSingleton<Application.Interfaces.IReadCache>(sp => sp.GetRequiredService<ReadCache>());
    services.AddSingleton<FleetSynchronizationOperation>();
    services.AddSingleton<Features.Synchronization.Interfaces.IFleetSynchronizationOperation>(sp => sp.GetRequiredService<FleetSynchronizationOperation>());
    services.AddSingleton<Features.Synchronization.Interfaces.ISynchronizationStatusProvider>(sp => sp.GetRequiredService<FleetSynchronizationOperation>());
    services.AddSingleton<Features.Synchronization.Interfaces.IPlanningRefreshOperation, PlanningRefreshOperation>();
    services.AddSingleton<Features.Synchronization.Interfaces.IEtaRefreshOperation, EtaRefreshOperation>();
    services.AddSingleton<Features.Synchronization.Interfaces.ITruckHistoryOperation, TruckHistoryOperation>();
    services.AddSingleton<TruckHistoryQueue>();
    services.AddSingleton<RouteDisplayCache>();
    services.AddSingleton<ServerTelemetry>();
    services.AddScoped<RoutePlanningService>();
    services.AddScoped<TruckPlanningProfileService>();
    services.AddScoped<RoutePlanStore>();
    services.AddScoped<BaseRouteService>();
    services.AddScoped<StopAddressService>();
    services.AddScoped<DeadheadService>();
    services.AddScoped<Application.Features.Dispatch.Services.DispatchRates>();
    services.AddScoped<RouteRecalculationBudget>();
    services.AddSingleton<Features.Synchronization.Interfaces.IBaseRouteOperation, BaseRouteOperation>();
    services.AddSingleton<Features.Routing.Interfaces.IRouteRequestValidator, RouteRequestValidator>();
    services.AddSingleton<Features.Routing.Interfaces.IRouteSectionValidator, Features.Routing.Algorithms.RouteSectionValidator>();
    services.AddScoped<PlanningSettingsService>();
    services.AddScoped<FuelRegionPlanner>();
    services.AddScoped<FuelPlanningService>();
    services.AddScoped<FuelHorizon>();
    services.AddScoped<TruckFuelPlans>();
    services.AddSingleton<FuelPlanMemory>();
    services.AddScoped<FuelScheduleEvaluator>();
    services.AddScoped<AutomaticPlanningService>();
    services.AddScoped<PlanningReadService>();
    services.AddScoped<Application.Features.Eta.Services.EtaService>();
    services.AddScoped<Application.Features.Eta.Services.EtaChainInputsService>();
    services.AddScoped<Application.Features.Eta.Services.EtaForecastService>();
    services.AddSingleton<Application.Features.Eta.Services.EtaMemory>();
    services.AddScoped<RoutePreviewService>();
    services.AddSingleton<PlanningRefreshQueue>();
    services.AddSingleton<RoutePreparationQueue>();

    return services;
  }
}
