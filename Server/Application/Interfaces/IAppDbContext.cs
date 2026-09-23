using Domain.Entities.Border;
using Domain.Entities.Caching;
using Domain.Entities.Costs;
using Domain.Entities.Dispatch;
using Domain.Entities.Execution;
using Domain.Entities.Fleet;
using Domain.Entities.Fuel;
using Domain.Entities.Mileage;
using Domain.Entities.Shipments;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace Application.Interfaces;

public interface IAppDbContext
{
  DbSet<BorderCrossing> BorderCrossings { get; }
  DbSet<BorderSaveReceipt> BorderSaveReceipts { get; }
  DbSet<Shipment> Shipments { get; }
  DbSet<ShipmentSaveReceipt> ShipmentSaveReceipts { get; }
  DbSet<Company> Companies { get; }
  DbSet<User> Users { get; }
  DbSet<FuelStation> FuelStations { get; }
  DbSet<TruckFuelPlan> TruckFuelPlans { get; }
  DbSet<FuelVisitSend> FuelVisitSends { get; }
  DbSet<FuelDiscount> FuelDiscounts { get; }
  DbSet<FuelTransaction> FuelTransactions { get; }
  DbSet<FuelImportSource> FuelImportSources { get; }
  DbSet<IftaTaxRate> IftaTaxRates { get; }
  DbSet<Truck> Trucks { get; }
  DbSet<Trailer> Trailers { get; }
  DbSet<Driver> Drivers { get; }
  DbSet<Customer> Customers { get; }
  DbSet<Dispatch> Dispatches { get; }
  DbSet<DispatchSourceLink> DispatchSourceLinks { get; }
  DbSet<DispatchNumberCounter> DispatchNumberCounters { get; }
  DbSet<DispatchStop> DispatchStops { get; }
  DbSet<DispatchWorkspace> DispatchWorkspaces { get; }
  DbSet<DispatchWorkspaceRevision> DispatchWorkspaceRevisions { get; }
  DbSet<DispatchActivityThread> DispatchActivityThreads { get; }
  DbSet<DispatchActivityEntry> DispatchActivityEntries { get; }
  DbSet<DispatchDocument> DispatchDocuments { get; }
  DbSet<ExecutionLeg> ExecutionLegs { get; }
  DbSet<ExecutionLegStop> ExecutionLegStops { get; }
  DbSet<ExecutionLegRevision> ExecutionLegRevisions { get; }
  DbSet<LoadExecutionLeg> LoadExecutionLegs { get; }
  DbSet<DispatchSwitchOperation> DispatchSwitchOperations { get; }
  DbSet<Trip> Trips { get; }
  DbSet<SwitchParticipant> SwitchParticipants { get; }
  DbSet<TrailerCustodyInterval> TrailerCustodyIntervals { get; }
  DbSet<ExecutionActionReceipt> ExecutionActionReceipts { get; }
  DbSet<ExecutionPlanningChange> ExecutionPlanningChanges { get; }
  DbSet<ExecutionSourceReceipt> ExecutionSourceReceipts { get; }
  DbSet<Movement> Movements { get; }
  DbSet<MovementDistanceEvidence> MovementDistanceEvidence { get; }
  DbSet<MovementAllocationEvent> MovementAllocationEvents { get; }
  DbSet<DriverHosReading> DriverHosReadings { get; }
  DbSet<TruckLocationReading> TruckLocationReadings { get; }
  DbSet<CacheInvalidation> CacheInvalidations { get; }
  DbSet<Expense> Expenses { get; }
  DbSet<ExpenseAttribution> ExpenseAttributions { get; }
  DbSet<ExpenseAttributionEvent> ExpenseAttributionEvents { get; }
  DbSet<MileageAllocationPolicy> MileageAllocationPolicies { get; }
  DbSet<OdometerPosition> OdometerPositions { get; }
  DbSet<OdometerCaptureCheckpoint> OdometerCaptureCheckpoints { get; }
  DbSet<OdometerInterval> OdometerIntervals { get; }
  DbSet<MileageCaptureGap> MileageCaptureGaps { get; }
  DbSet<DispatchStopCompletionEvent> DispatchStopCompletionEvents { get; }
  DbSet<DispatchSettings> DispatchSettings { get; }
  DbSet<TruckPlanningProfile> TruckPlanningProfiles { get; }
  DbSet<FleetPlanningSettings> FleetPlanningSettings { get; }
  DbSet<SynchronizationCheckpoint> SynchronizationCheckpoints { get; }
  DbSet<RouteGeometryChange> RouteGeometryChanges { get; }
  DbSet<RouteMovementChunk> RouteMovementChunks { get; }
  DbSet<RouteGeometryChunk> RouteGeometryChunks { get; }
  DbSet<DispatchRoutePlan> DispatchRoutePlans { get; }
  DbSet<DispatchBaseRoute> DispatchBaseRoutes { get; }
  DbSet<DispatchRouteChoice> DispatchRouteChoices { get; }
  DbSet<DispatchRoutePreview> DispatchRoutePreviews { get; }
  DbSet<DispatchDeadhead> DispatchDeadheads { get; }
  DbSet<DispatchRate> DispatchRates { get; }
  DbSet<RoutingApiCall> RoutingApiCalls { get; }
  DbSet<RouteRecalculationAttempt> RouteRecalculationAttempts { get; }

  DatabaseFacade Database { get; }

  // Locks require an active transaction and are released when it ends.
  Task LockRouteBudgetAsync(CancellationToken ct);
  Task LockFuelImportAsync(CancellationToken ct);
  Task LockDispatchRatesAsync(CancellationToken ct);
  bool IsWriteConflict(Exception exception);
  Task<bool> LockExecutionLegAsync(
    Guid executionLegId,
    long expectedRevision,
    CancellationToken ct
  ) => throw new NotSupportedException("Execution-leg locking is unsupported.");

  EntityEntry Entry(object entity);

  Task<int> SaveChangesAsync(CancellationToken ct);
}
