using Domain.Entities.Dispatch;
using Domain.Entities.Fleet;
using Domain.Entities.Fuel;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace Application.Interfaces;

public interface IAppDbContext
{
  DbSet<User> Users { get; }
  DbSet<FuelStation> FuelStations { get; }
  DbSet<FuelDiscount> FuelDiscounts { get; }
  DbSet<FuelTransaction> FuelTransactions { get; }
  DbSet<FuelImportSource> FuelImportSources { get; }
  DbSet<IftaTaxRate> IftaTaxRates { get; }
  DbSet<Truck> Trucks { get; }
  DbSet<Trailer> Trailers { get; }
  DbSet<Driver> Drivers { get; }
  DbSet<Customer> Customers { get; }
  DbSet<Dispatch> Dispatches { get; }
  DbSet<DispatchStop> DispatchStops { get; }
  DbSet<DispatchSettings> DispatchSettings { get; }
  DbSet<TruckPlanningProfile> TruckPlanningProfiles { get; }
  DbSet<FleetPlanningSettings> FleetPlanningSettings { get; }
  DbSet<SynchronizationCheckpoint> SynchronizationCheckpoints { get; }
  DbSet<DispatchRoutePlan> DispatchRoutePlans { get; }
  DbSet<DispatchBaseRoute> DispatchBaseRoutes { get; }
  DbSet<DispatchDeadhead> DispatchDeadheads { get; }
  DbSet<DispatchRate> DispatchRates { get; }
  DbSet<RoutingApiCall> RoutingApiCalls { get; }
  DbSet<RouteRecalculationAttempt> RouteRecalculationAttempts { get; }

  DatabaseFacade Database { get; }

  // Locks require an active transaction and are released when it ends.
  Task LockRouteBudgetAsync(CancellationToken ct);
  Task LockFuelImportAsync(CancellationToken ct);
  Task LockDispatchRatesAsync(CancellationToken ct);

  EntityEntry Entry(object entity);

  Task<int> SaveChangesAsync(CancellationToken ct);
}
