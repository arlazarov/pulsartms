using Application.Interfaces;
using Domain.Entities;
using Domain.Entities.Dispatch;
using Domain.Entities.Fleet;
using Domain.Entities.Fuel;
using Infrastructure.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.DataProtection.EntityFrameworkCore;

namespace Infrastructure.Persistence;

public partial class AppDbContext(DbContextOptions<AppDbContext> options)
  : IdentityDbContext<AppUser>(options),
    IAppDbContext, IDataProtectionKeyContext
{
  public DbSet<DataProtectionKey> DataProtectionKeys => Set<DataProtectionKey>();
  public DbSet<IntegrationCredentialSetting> IntegrationCredentialSettings => Set<IntegrationCredentialSetting>();
  public new DbSet<User> Users => Set<User>();
  public DbSet<FuelStation> FuelStations => Set<FuelStation>();
  public DbSet<FuelDiscount> FuelDiscounts => Set<FuelDiscount>();
  public DbSet<FuelTransaction> FuelTransactions => Set<FuelTransaction>();
  public DbSet<FuelImportSource> FuelImportSources => Set<FuelImportSource>();
  public DbSet<IftaTaxRate> IftaTaxRates => Set<IftaTaxRate>();
  public DbSet<Truck> Trucks => Set<Truck>();
  public DbSet<Trailer> Trailers => Set<Trailer>();
  public DbSet<Driver> Drivers => Set<Driver>();
  public DbSet<Customer> Customers => Set<Customer>();
  public DbSet<Dispatch> Dispatches => Set<Dispatch>();
  public DbSet<DispatchStop> DispatchStops => Set<DispatchStop>();
  public DbSet<DispatchSettings> DispatchSettings => Set<DispatchSettings>();
  public DbSet<TruckPlanningProfile> TruckPlanningProfiles => Set<TruckPlanningProfile>();
  public DbSet<FleetPlanningSettings> FleetPlanningSettings => Set<FleetPlanningSettings>();
  public DbSet<SynchronizationCheckpoint> SynchronizationCheckpoints => Set<SynchronizationCheckpoint>();
  public DbSet<DispatchRoutePlan> DispatchRoutePlans => Set<DispatchRoutePlan>();
  public DbSet<DispatchBaseRoute> DispatchBaseRoutes => Set<DispatchBaseRoute>();
  public DbSet<DispatchDeadhead> DispatchDeadheads => Set<DispatchDeadhead>();
  public DbSet<DispatchRate> DispatchRates => Set<DispatchRate>();
  public DbSet<RoutingApiCall> RoutingApiCalls => Set<RoutingApiCall>();
  public DbSet<RouteRecalculationAttempt> RouteRecalculationAttempts => Set<RouteRecalculationAttempt>();

  public Task LockRouteBudgetAsync(CancellationToken ct) => Database.IsNpgsql()
    ? Database.ExecuteSqlRawAsync("SELECT pg_advisory_xact_lock(710246711)", ct)
    : Task.CompletedTask;

  public Task LockFuelImportAsync(CancellationToken ct) => Database.IsNpgsql()
    ? Database.ExecuteSqlRawAsync("SELECT pg_advisory_xact_lock(714092601)", ct)
    : Task.CompletedTask;

  public Task LockDispatchRatesAsync(CancellationToken ct) => Database.IsNpgsql()
    ? Database.ExecuteSqlRawAsync("SELECT pg_advisory_xact_lock(710246712)", ct)
    : Task.CompletedTask;
}
