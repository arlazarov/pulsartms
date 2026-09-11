using Application.Features.Synchronization.Interfaces;
using Application.Features.Routing.Interfaces;
using Application.Features.Routing.Options;
using Application.Features.Auth.Interfaces;
using Infrastructure.Synchronization;
using Infrastructure.Integrations.TomTom;
using Application.Features.Dispatch.Interfaces;
using Application.Features.Fleet.Interfaces;
using Application.Features.Fuel.Interfaces;
using Application.Interfaces;
using Infrastructure.Identity;
using Infrastructure.Integrations.Bvd;
using Infrastructure.Integrations.Samsara;
using Infrastructure.Integrations.Torque;
using Infrastructure.Persistence;
using Infrastructure.Integrations.Google.Gmail;
using Infrastructure.Integrations.Ifta;
using Infrastructure.Integrations.Google.Places;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.AspNetCore.DataProtection;

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

    services.AddHostedService<DatabaseInitializer>();
    services.AddTransient<Microsoft.AspNetCore.Hosting.IStartupFilter, SessionStartupFilter>();
    services.AddHealthChecks().AddCheck<DatabaseHealthCheck>("database", tags: ["ready"]);

    services.AddDataProtection().SetApplicationName("AMFTMS")
      .PersistKeysToDbContext<AppDbContext>();

    services
      .AddAuthentication(IdentityConstants.BearerScheme)
      .AddBearerToken(IdentityConstants.BearerScheme);

    services.AddAuthorization(options =>
    {
      options.AddPolicy("Admin", policy => policy.RequireAuthenticatedUser().AddRequirements(new AdminRequirement()));
      options.FallbackPolicy = new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build();
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
    services.AddHttpContextAccessor();

    services.AddScoped<IAppDbContext>(provider => provider.GetRequiredService<AppDbContext>());
    services.AddSingleton<Application.Features.Integrations.Interfaces.IIntegrationCredentialStore, IntegrationCredentialStore>();
    services.AddSingleton<Application.Features.Integrations.Interfaces.IIntegrationDeploymentCredentials, Infrastructure.Integrations.IntegrationDeploymentCredentials>();
    services.AddScoped<IDeadheadHistoryReader, DeadheadHistoryReader>();
    services.AddScoped<INextLoadRouteReader, NextLoadRouteReader>();
    services.AddScoped<ITruckFuelPlanStore, TruckFuelPlanStore>();
    services.AddScoped<Application.Features.Eta.Interfaces.IEtaRootRouteReader, EtaRootRouteReader>();
    services.AddScoped<Application.Features.Eta.Interfaces.IEtaForecastStore, EtaForecastStore>();

    services.AddScoped<IIdentityService, IdentityService>();
    services.AddScoped<IUserRoleService, UserRoleService>();
    services.AddScoped<ICurrentUser, CurrentUser>();
    services.AddScoped<IAuthService, AuthService>();

    services.AddScoped<GmailServiceFactory>();
    services.AddScoped<Application.Features.Fuel.Interfaces.IGmailPushValidator, GmailPushValidator>();
    services.AddScoped<GmailAttachmentService>();
    services.AddScoped<IGmailWatchService, GmailWatchService>();
    services.AddScoped<IGmailWatchStore, GmailWatchStore>();
    services.AddScoped<IFuelStationLookupStore, Infrastructure.Integrations.Google.Places.FuelStationLookupStore>();
    if (configuration.GetValue("Gmail:BackgroundMaintenanceEnabled", true))
      services.AddHostedService<ApplicationWorker<IGmailWatchOperation>>();
    services.AddScoped<IFuelDiscountProvider, BvdFuelDiscountProvider>();


    services.AddScoped<ISynchronizationStore, SynchronizationStore>();
    services.AddHostedService<ApplicationWorker<IFleetSynchronizationOperation>>();

    services.AddHttpClient<IPlaceSearchService, GooglePlacesService>();
    services.AddHttpClient<IIftaApiService, IftaApiService>();

    services.AddSingleton<Application.Features.Eta.Interfaces.IRouteRegionLookup, Infrastructure.Eta.RouteRegionLookup>();
    services.AddHostedService<ApplicationWorker<IEtaRefreshOperation>>();
    services.AddHostedService<ApplicationWorker<ITruckHistoryOperation>>();
    services.AddHttpClient<SamsaraApiService>();
    services.AddSingleton<SamsaraHosHistoryCache>();
    services.AddSingleton<SamsaraDriverCatalogCache>();
    services.AddScoped<ITruckCameraProvider, SamsaraTruckCameraProvider>();
    services.AddScoped<IFleetProvider, SamsaraFleetProvider>();
    services.AddScoped<IDriverHosProvider, SamsaraDriverHosProvider>();
    services.AddScoped<Application.Features.Eta.Interfaces.IHosHistoryProvider, SamsaraHosHistoryProvider>();
    services.AddScoped<IFleetTelemetryProvider, SamsaraFleetTelemetryProvider>();
    services.AddScoped<IFleetTelemetryFeedProvider, SamsaraFleetTelemetryProvider>();

    services.AddHttpClient<IAddressGeocoder, GoogleAddressGeocoder>(client => client.Timeout = TimeSpan.FromSeconds(15)).RemoveAllLoggers();
    services.AddHttpClient<IRoutingProvider, TomTomRoutingProvider>(client => client.Timeout = TimeSpan.FromSeconds(30)).RemoveAllLoggers();
    services.AddHostedService<ApplicationWorker<IPlanningRefreshOperation>>();
    services.AddHostedService<ApplicationWorker<IBaseRouteOperation>>();

    services.AddHttpClient<TorqueApiService>();
    services.AddScoped<IDispatchProvider, TorqueDispatchProvider>();

    return services;
  }
}
