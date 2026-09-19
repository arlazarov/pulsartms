using Application.Behaviors;
using Application.Caching;
using Application.Features.Dispatch.Services;
using Application.Features.Eta.Background;
using Application.Features.Eta.Services;
using Application.Features.Execution.Background;
using Application.Features.Execution.Interfaces;
using Application.Features.Execution.Services;
using Application.Features.Fleet.Background;
using Application.Features.Fleet.Interfaces;
using Application.Features.Fleet.Queries.GetFleetLocations;
using Application.Features.Fleet.Services;
using Application.Features.Fuel.Background;
using Application.Features.Fuel.Services;
using Application.Features.Integrations.Interfaces;
using Application.Features.Integrations.Services;
using Application.Features.Mileage.Background;
using Application.Features.Mileage.Interfaces;
using Application.Features.Mileage.Services;
using Application.Features.Routing.Algorithms;
using Application.Features.Routing.Background;
using Application.Features.Routing.Interfaces;
using Application.Features.Routing.Services;
using Application.Features.Routing.Services.Addresses;
using Application.Features.Routing.Services.Deadheads;
using Application.Features.Routing.Services.FuelPlanning;
using Application.Features.Routing.Services.Routes;
using Application.Features.Synchronization.Interfaces;
using Application.Features.Synchronization.Services;
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
    services.AddScoped<IntegrationSettingsService>();
    services.AddScoped<IIntegrationCredentials>(sp =>
      sp.GetRequiredService<IntegrationSettingsService>()
    );
    services.AddSingleton<FleetTelemetryCache>();
    services.AddSingleton<DriverHosSnapshot>();
    services.AddSingleton<IDriverHosProvider>(sp =>
      sp.GetRequiredService<DriverHosSnapshot>()
    );
    services.AddSingleton<
      IDriverHosRefreshOperation,
      DriverHosRefreshOperation
    >();
    services.AddSingleton<FleetLocationStream>();
    services.AddMemoryCache();
    services.AddSingleton(TimeProvider.System);
    services.AddScoped<GmailWatchLifecycle>();
    services.AddScoped<FuelStationLookupService>();
    services.AddScoped<FuelExchangeRateService>();
    services.AddSingleton<IGmailWatchOperation, GmailWatchOperation>();
    services.AddSingleton<ReadCache>();
    services.AddSingleton<IReadCache>(sp => sp.GetRequiredService<ReadCache>());
    services.AddSingleton<FleetSynchronizationOperation>();
    services.AddSingleton<IFleetSynchronizationOperation>(sp =>
      sp.GetRequiredService<FleetSynchronizationOperation>()
    );
    services.AddSingleton<ISynchronizationStatusProvider>(sp =>
      sp.GetRequiredService<FleetSynchronizationOperation>()
    );
    services.AddSingleton<
      IPlanningRefreshOperation,
      PlanningRefreshOperation
    >();
    services.AddSingleton<IEtaRefreshOperation, EtaRefreshOperation>();
    services.AddSingleton<
      IExecutionPlanningOperation,
      ExecutionPlanningOperation
    >();
    services.AddSingleton<ITruckHistoryOperation, TruckHistoryOperation>();
    services.AddSingleton<TruckHistoryQueue>();
    services.AddSingleton<RouteDisplayCache>();
    services.AddSingleton<ServerTelemetry>();
    services.AddScoped<RoutePlanningService>();
    services.AddScoped<IPlannedRouteReader>(sp =>
      sp.GetRequiredService<RoutePlanningService>()
    );
    services.AddScoped<TruckPlanningProfileService>();
    services.AddScoped<RoutePlanStore>();
    services.AddScoped<BaseRouteService>();
    services.AddScoped<RouteChoiceService>();
    services.AddScoped<RouteChoiceDrafts>();
    services.AddScoped<StopAddressService>();
    services.AddScoped<ExecutionStopAddressService>();
    services.AddScoped<DeadheadHistoryService>();
    services.AddScoped<DeadheadHistoryPublication>();
    services.AddScoped<DeadheadService>();
    services.AddScoped<DispatchRates>();
    services.AddScoped<IAutomaticMileageRecorder, AutomaticMileageRecorder>();
    services.AddSingleton<
      IOdometerCaptureOperation,
      OdometerCaptureOperation
    >();
    services.AddScoped<RouteRecalculationBudget>();
    services.AddSingleton<IBaseRouteOperation, BaseRouteOperation>();
    services.AddSingleton<IRouteRequestValidator, RouteRequestValidator>();
    services.AddSingleton<IRouteSectionValidator, RouteSectionValidator>();
    services.AddScoped<PlanningSettingsService>();
    services.AddScoped<FuelRegionPlanner>();
    services.AddScoped<FuelPlanningService>();
    services.AddScoped<FuelPriceRefreshService>();
    services.AddScoped<FuelHorizon>();
    services.AddScoped<IFuelWorkInputsReader, FuelWorkInputsReader>();
    services.AddScoped<TruckFuelPlans>();
    services.AddSingleton<FuelPlanMemory>();
    services.AddScoped<FuelScheduleEvaluator>();
    services.AddScoped<AutomaticPlanningService>();
    services.AddScoped<PlanningReadService>();
    services.AddScoped<TruckPlanningInputsReader>();
    services.AddScoped<PlanningWorkPublication>();
    services.AddScoped<ISavedRoadValidation, SavedRoadValidation>();
    services.AddScoped<IFuelSavedInputsValidation, FuelSavedInputsValidation>();
    services.AddScoped<EtaService>();
    services.AddScoped<EtaChainInputsService>();
    services.AddScoped<TruckItineraryReader>();
    services.AddScoped<EtaForecastService>();
    services.AddSingleton<EtaMemory>();
    services.AddScoped<RoutePreviewService>();
    services.AddScoped<PlanningRefreshQueue>();
    services.AddSingleton<PlanningRefreshSignal>();
    services.AddSingleton<RoutePreparationQueue>();
    services.AddScoped<SourceRoadInputs>();
    services.AddScoped<SourceRoadDemand>();

    return services;
  }
}
