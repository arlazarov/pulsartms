using Application.Features.Addresses.Interfaces;
using Application.Features.Auth.Interfaces;
using Application.Features.Border.Interfaces;
using Application.Features.Dispatch.Interfaces;
using Application.Features.Eta.Interfaces;
using Application.Features.Execution.Interfaces;
using Application.Features.Fleet.Interfaces;
using Application.Features.Fuel.Interfaces;
using Application.Features.Integrations.Interfaces;
using Application.Features.Mileage.Interfaces;
using Application.Features.Routing.Interfaces;
using Application.Features.Routing.Options;
using Application.Features.Synchronization.Interfaces;
using Application.Interfaces;
using Infrastructure.Identity;
using Infrastructure.Integrations;
using Infrastructure.Integrations.BankOfCanada;
using Infrastructure.Integrations.Bvd;
using Infrastructure.Integrations.GeoTimeZone;
using Infrastructure.Integrations.Google.Gmail;
using Infrastructure.Integrations.Google.Places;
using Infrastructure.Integrations.Google.Weather;
using Infrastructure.Integrations.Ifta;
using Infrastructure.Integrations.Samsara;
using Infrastructure.Integrations.TomTom;
using Infrastructure.Integrations.Torque;
using Infrastructure.Persistence;
using Infrastructure.Synchronization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Infrastructure;

public static class DependencyInjection
{
  public static IServiceCollection AddInfrastructure(
    this IServiceCollection services,
    IConfiguration configuration
  )
  {
    services.AddDbContext<AppDbContext>(options =>
    {
      options.UseNpgsql(configuration.GetConnectionString("DefaultConnection"));
    });

    services.AddScoped<IBorderDataProtection, BorderDataProtection>();
    services.AddHostedService<DatabaseInitializer>();
    services.AddTransient<IStartupFilter, SessionStartupFilter>();
    services
      .AddHealthChecks()
      .AddCheck<DatabaseHealthCheck>("database", tags: ["ready"]);

    services
      .AddDataProtection()
      .SetApplicationName("AMFTMS")
      .PersistKeysToDbContext<AppDbContext>();

    services
      .AddAuthentication(IdentityConstants.BearerScheme)
      .AddBearerToken(IdentityConstants.BearerScheme);

    services.AddAuthorization(options =>
    {
      options.AddPolicy(
        "Admin",
        policy =>
          policy
            .RequireAuthenticatedUser()
            .AddRequirements(new AdminRequirement())
      );
      options.AddPolicy(
        "Dispatch",
        policy =>
          policy
            .RequireAuthenticatedUser()
            .AddRequirements(new DispatchRequirement())
      );
      options.FallbackPolicy = new AuthorizationPolicyBuilder()
        .RequireAuthenticatedUser()
        .Build();
    });

    services
      .AddIdentityCore<AppUser>(options =>
      {
        options.Password.RequiredLength = 8;
        options.Password.RequiredUniqueChars = 1;
        options.Password.RequireDigit = false;
        options.Password.RequireLowercase = false;
        options.Password.RequireUppercase = false;
        options.Password.RequireNonAlphanumeric = false;
        options.Lockout.AllowedForNewUsers = true;
        options.Lockout.MaxFailedAccessAttempts = 5;
        options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
      })
      .AddEntityFrameworkStores<AppDbContext>()
      .AddSignInManager()
      .AddDefaultTokenProviders();

    services.AddScoped<IAuthorizationHandler, AdminAuthorizationHandler>();
    services.AddScoped<IAuthorizationHandler, DispatchAuthorizationHandler>();
    services.AddHttpContextAccessor();

    services.AddScoped<IAppDbContext>(provider =>
      provider.GetRequiredService<AppDbContext>()
    );
    services.AddSingleton<
      IIntegrationCredentialStore,
      IntegrationCredentialStore
    >();
    services.AddSingleton<
      IIntegrationDeploymentCredentials,
      IntegrationDeploymentCredentials
    >();
    services.AddScoped<IDeadheadHistoryReader, DeadheadHistoryReader>();
    services.AddScoped<IOdometerCaptureLease, OdometerCaptureLease>();
    services.AddScoped<IOdometerFeedProvider, SamsaraOdometerProvider>();
    services.AddHostedService<ApplicationWorker<IOdometerCaptureOperation>>();
    services.AddScoped<INextLoadRouteReader, NextLoadRouteReader>();
    services.AddScoped<ITruckFuelPlanStore, TruckFuelPlanStore>();
    services.AddScoped<IFuelExchangeRateStore, FuelExchangeRateStore>();
    services.AddScoped<ISavedRoutePlanReader, SavedRoutePlanReader>();
    services.AddScoped<IEtaForecastStore, EtaForecastStore>();
    services.AddScoped<IExecutionPlanningStore, ExecutionPlanningStore>();
    services.AddScoped<IPlanningRefreshStore, PlanningRefreshStore>();
    services.AddScoped<ISourceRoadStore, SourceRoadStore>();
    services.AddScoped<IExecutionReadScope, ExecutionReadScope>();
    services.AddScoped<IPlanningPublicationScope, PlanningPublicationScope>();
    services.AddHostedService<ApplicationWorker<IExecutionPlanningOperation>>();

    services.AddScoped<IIdentityService, IdentityService>();
    services.AddScoped<IUserRoleService, UserRoleService>();
    services.AddScoped<ICurrentUser, CurrentUser>();
    services.AddScoped<IAuthService, AuthService>();

    services.AddScoped<GmailServiceFactory>();
    services.AddScoped<IGmailPushValidator, GmailPushValidator>();
    services.AddScoped<GmailAttachmentService>();
    services.AddScoped<IGmailWatchService, GmailWatchService>();
    services.AddScoped<IGmailWatchStore, GmailWatchStore>();
    services.AddScoped<IFuelStationLookupStore, FuelStationLookupStore>();
    if (configuration.GetValue("Gmail:BackgroundMaintenanceEnabled", true))
      services.AddHostedService<ApplicationWorker<IGmailWatchOperation>>();
    services.AddScoped<IFuelDiscountProvider, BvdFuelDiscountProvider>();

    services.AddScoped<ISynchronizationStore, SynchronizationStore>();
    services.AddHostedService<
      ApplicationWorker<IFleetSynchronizationOperation>
    >();

    services.AddHttpClient<IPlaceSearchService, GooglePlacesService>(client =>
      client.Timeout = TimeSpan.FromSeconds(10)
    );
    services
      .AddHttpClient<IWeatherProvider, GoogleWeatherProvider>(client =>
        client.Timeout = TimeSpan.FromSeconds(10)
      )
      .RemoveAllLoggers();
    services.AddHttpClient<IIftaApiService, IftaApiService>(client =>
      client.Timeout = TimeSpan.FromSeconds(30)
    );
    services
      .AddHttpClient<
        IFuelExchangeRateProvider,
        BankOfCanadaExchangeRateProvider
      >(client => client.Timeout = TimeSpan.FromSeconds(10))
      .RemoveAllLoggers();

    services.AddSingleton<IRouteRegionLookup, RouteRegionLookup>();
    services.AddHostedService<ApplicationWorker<IEtaRefreshOperation>>();
    services.AddHostedService<ApplicationWorker<ITruckHistoryOperation>>();
    services
      .AddHttpClient<SamsaraApiService>(client =>
        client.Timeout = TimeSpan.FromSeconds(30)
      )
      .RemoveAllLoggers();
    services.AddSingleton<SamsaraHosHistoryCache>();
    services.AddSingleton<SamsaraDriverCatalogCache>();
    services.AddScoped<ITruckCameraProvider, SamsaraTruckCameraProvider>();
    services.AddScoped<IFleetProvider, SamsaraFleetProvider>();
    services.AddScoped<IDriverHosRefreshProvider, SamsaraDriverHosProvider>();
    services.AddHostedService<ApplicationWorker<IDriverHosRefreshOperation>>();
    services.AddScoped<IHosHistoryProvider, SamsaraHosHistoryProvider>();
    services.AddScoped<
      IFleetTelemetryProvider,
      SamsaraFleetTelemetryProvider
    >();
    services.AddScoped<
      IFleetTelemetryFeedProvider,
      SamsaraFleetTelemetryProvider
    >();

    services
      .AddHttpClient<IAddressSuggestionsProvider, GoogleAddressSuggestions>(
        client => client.Timeout = TimeSpan.FromSeconds(5)
      )
      .RemoveAllLoggers();
    services
      .AddHttpClient<IAddressGeocoder, GoogleAddressGeocoder>(client =>
        client.Timeout = TimeSpan.FromSeconds(15)
      )
      .RemoveAllLoggers();
    services
      .AddHttpClient<IRoutingProvider, TomTomRoutingProvider>(client =>
        client.Timeout = TimeSpan.FromSeconds(30)
      )
      .RemoveAllLoggers();
    services
      .AddHttpClient<IRouteAlternativesProvider, TomTomRoutingProvider>(
        client => client.Timeout = TimeSpan.FromSeconds(30)
      )
      .RemoveAllLoggers();
    services.AddHostedService<ApplicationWorker<IPlanningRefreshOperation>>();
    services.AddHostedService<ApplicationWorker<IBaseRouteOperation>>();

    var dispatchImport = configuration["DispatchImport:Provider"]?.Trim();
    if (!string.IsNullOrEmpty(dispatchImport))
    {
      if (
        !dispatchImport.Equals("torqueai", StringComparison.OrdinalIgnoreCase)
      )
        throw new InvalidOperationException(
          "Unsupported dispatch import provider."
        );
      services.AddHttpClient<TorqueApiService>();
      services.AddScoped<IDispatchProvider, TorqueDispatchProvider>();
    }

    return services;
  }
}
