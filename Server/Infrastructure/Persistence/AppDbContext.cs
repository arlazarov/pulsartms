using Application.Interfaces;
using Domain.Entities;
using Domain.Entities.Border;
using Domain.Entities.Caching;
using Domain.Entities.Costs;
using Domain.Entities.Dispatch;
using Domain.Entities.Execution;
using Domain.Entities.Fleet;
using Domain.Entities.Fuel;
using Domain.Entities.Mileage;
using Domain.Entities.Shipments;
using Infrastructure.Identity;
using Microsoft.AspNetCore.DataProtection.EntityFrameworkCore;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Infrastructure.Persistence;

public partial class AppDbContext(DbContextOptions<AppDbContext> options)
  : IdentityDbContext<AppUser>(options),
    IAppDbContext,
    IDataProtectionKeyContext
{
  public DbSet<Company> Companies => Set<Company>();
  public DbSet<BorderCrossing> BorderCrossings => Set<BorderCrossing>();
  public DbSet<BorderSaveReceipt> BorderSaveReceipts =>
    Set<BorderSaveReceipt>();
  public DbSet<Shipment> Shipments => Set<Shipment>();
  public DbSet<ShipmentSaveReceipt> ShipmentSaveReceipts =>
    Set<ShipmentSaveReceipt>();
  public DbSet<DataProtectionKey> DataProtectionKeys =>
    Set<DataProtectionKey>();
  public DbSet<IntegrationCredentialSetting> IntegrationCredentialSettings =>
    Set<IntegrationCredentialSetting>();
  public new DbSet<User> Users => Set<User>();
  public DbSet<FuelStation> FuelStations => Set<FuelStation>();
  public DbSet<TruckFuelPlan> TruckFuelPlans => Set<TruckFuelPlan>();
  public DbSet<FuelVisitSend> FuelVisitSends => Set<FuelVisitSend>();
  public DbSet<FuelDiscount> FuelDiscounts => Set<FuelDiscount>();
  public DbSet<FuelTransaction> FuelTransactions => Set<FuelTransaction>();
  public DbSet<FuelImportSource> FuelImportSources => Set<FuelImportSource>();
  public DbSet<IftaTaxRate> IftaTaxRates => Set<IftaTaxRate>();
  public DbSet<Truck> Trucks => Set<Truck>();
  public DbSet<Trailer> Trailers => Set<Trailer>();
  public DbSet<Driver> Drivers => Set<Driver>();
  public DbSet<Customer> Customers => Set<Customer>();
  public DbSet<Dispatch> Dispatches => Set<Dispatch>();
  public DbSet<PlanningRefreshRequest> PlanningRefreshRequests =>
    Set<PlanningRefreshRequest>();
  public DbSet<SourceRoadRequest> SourceRoadRequests =>
    Set<SourceRoadRequest>();
  public DbSet<PlanningInputRevision> PlanningInputRevisions =>
    Set<PlanningInputRevision>();
  public DbSet<DispatchStop> DispatchStops => Set<DispatchStop>();
  public DbSet<DispatchWorkspace> DispatchWorkspaces =>
    Set<DispatchWorkspace>();
  public DbSet<DispatchWorkspaceRevision> DispatchWorkspaceRevisions =>
    Set<DispatchWorkspaceRevision>();
  public DbSet<DispatchActivityThread> DispatchActivityThreads =>
    Set<DispatchActivityThread>();
  public DbSet<DispatchActivityEntry> DispatchActivityEntries =>
    Set<DispatchActivityEntry>();
  public DbSet<DispatchDocument> DispatchDocuments => Set<DispatchDocument>();
  public DbSet<ExecutionLeg> ExecutionLegs => Set<ExecutionLeg>();
  public DbSet<ExecutionLegStop> ExecutionLegStops => Set<ExecutionLegStop>();
  public DbSet<ExecutionLegRevision> ExecutionLegRevisions =>
    Set<ExecutionLegRevision>();
  public DbSet<LoadExecutionLeg> LoadExecutionLegs => Set<LoadExecutionLeg>();
  public DbSet<DispatchSwitchOperation> DispatchSwitchOperations =>
    Set<DispatchSwitchOperation>();
  public DbSet<Trip> Trips => Set<Trip>();
  public DbSet<SwitchParticipant> SwitchParticipants =>
    Set<SwitchParticipant>();
  public DbSet<TrailerCustodyInterval> TrailerCustodyIntervals =>
    Set<TrailerCustodyInterval>();
  public DbSet<ExecutionActionReceipt> ExecutionActionReceipts =>
    Set<ExecutionActionReceipt>();
  public DbSet<ExecutionPlanningChange> ExecutionPlanningChanges =>
    Set<ExecutionPlanningChange>();
  public DbSet<ExecutionSourceReceipt> ExecutionSourceReceipts =>
    Set<ExecutionSourceReceipt>();
  public DbSet<Movement> Movements => Set<Movement>();
  public DbSet<MovementDistanceEvidence> MovementDistanceEvidence =>
    Set<MovementDistanceEvidence>();
  public DbSet<MovementAllocationEvent> MovementAllocationEvents =>
    Set<MovementAllocationEvent>();
  public DbSet<DriverHosReading> DriverHosReadings => Set<DriverHosReading>();
  public DbSet<TruckLocationReading> TruckLocationReadings =>
    Set<TruckLocationReading>();
  public DbSet<CacheInvalidation> CacheInvalidations =>
    Set<CacheInvalidation>();
  public DbSet<Expense> Expenses => Set<Expense>();
  public DbSet<ExpenseAttribution> ExpenseAttributions =>
    Set<ExpenseAttribution>();
  public DbSet<ExpenseAttributionEvent> ExpenseAttributionEvents =>
    Set<ExpenseAttributionEvent>();
  public DbSet<MileageAllocationPolicy> MileageAllocationPolicies =>
    Set<MileageAllocationPolicy>();
  public DbSet<OdometerPosition> OdometerPositions => Set<OdometerPosition>();
  public DbSet<OdometerCaptureCheckpoint> OdometerCaptureCheckpoints =>
    Set<OdometerCaptureCheckpoint>();
  public DbSet<OdometerInterval> OdometerIntervals => Set<OdometerInterval>();
  public DbSet<MileageCaptureGap> MileageCaptureGaps =>
    Set<MileageCaptureGap>();
  public DbSet<DispatchStopCompletionEvent> DispatchStopCompletionEvents =>
    Set<DispatchStopCompletionEvent>();
  public DbSet<DispatchSettings> DispatchSettings => Set<DispatchSettings>();
  public DbSet<DispatchSourceLink> DispatchSourceLinks =>
    Set<DispatchSourceLink>();
  public DbSet<DispatchNumberCounter> DispatchNumberCounters =>
    Set<DispatchNumberCounter>();
  public DbSet<TruckPlanningProfile> TruckPlanningProfiles =>
    Set<TruckPlanningProfile>();
  public DbSet<FleetPlanningSettings> FleetPlanningSettings =>
    Set<FleetPlanningSettings>();
  public DbSet<SynchronizationCheckpoint> SynchronizationCheckpoints =>
    Set<SynchronizationCheckpoint>();
  public DbSet<RouteGeometryChange> RouteGeometryChanges =>
    Set<RouteGeometryChange>();
  public DbSet<RouteMovementChunk> RouteMovementChunks =>
    Set<RouteMovementChunk>();
  public DbSet<RouteGeometryChunk> RouteGeometryChunks =>
    Set<RouteGeometryChunk>();
  public DbSet<DispatchRoutePlan> DispatchRoutePlans =>
    Set<DispatchRoutePlan>();
  public DbSet<DispatchBaseRoute> DispatchBaseRoutes =>
    Set<DispatchBaseRoute>();
  public DbSet<DispatchRouteChoice> DispatchRouteChoices =>
    Set<DispatchRouteChoice>();
  public DbSet<DispatchRoutePreview> DispatchRoutePreviews =>
    Set<DispatchRoutePreview>();
  public DbSet<DispatchDeadhead> DispatchDeadheads => Set<DispatchDeadhead>();
  public DbSet<DispatchRate> DispatchRates => Set<DispatchRate>();
  public DbSet<RoutingApiCall> RoutingApiCalls => Set<RoutingApiCall>();
  public DbSet<RouteRecalculationAttempt> RouteRecalculationAttempts =>
    Set<RouteRecalculationAttempt>();

  public Task LockRouteBudgetAsync(CancellationToken ct) =>
    Database.IsNpgsql()
      ? Database.ExecuteSqlRawAsync(
        "SELECT pg_advisory_xact_lock(710246711)",
        ct
      )
      : Task.CompletedTask;

  public Task LockFuelImportAsync(CancellationToken ct) =>
    Database.IsNpgsql()
      ? Database.ExecuteSqlRawAsync(
        "SELECT pg_advisory_xact_lock(714092601)",
        ct
      )
      : Task.CompletedTask;

  public Task LockDispatchRatesAsync(CancellationToken ct) =>
    Database.IsNpgsql()
      ? Database.ExecuteSqlRawAsync(
        "SELECT pg_advisory_xact_lock(710246712)",
        ct
      )
      : Task.CompletedTask;

  public async Task<bool> LockExecutionLegAsync(
    Guid executionLegId,
    long expectedRevision,
    CancellationToken ct
  )
  {
    if (Database.CurrentTransaction is null)
      throw new InvalidOperationException(
        "Execution-leg locking requires an active transaction."
      );
    var changed = await ExecutionLegs
      .Where(x => x.Id == executionLegId && x.Revision == expectedRevision)
      .ExecuteUpdateAsync(
        properties => properties.SetProperty(x => x.Revision, x => x.Revision),
        ct
      );
    return changed == 1;
  }

  public bool IsWriteConflict(Exception exception)
  {
    for (
      Exception? current = exception;
      current is not null;
      current = current.InnerException
    )
      if (
        current is DbUpdateConcurrencyException
        || current
          is PostgresException
          {
            SqlState: PostgresErrorCodes.SerializationFailure
              or PostgresErrorCodes.DeadlockDetected
              or PostgresErrorCodes.UniqueViolation,
          }
      )
        return true;
    return false;
  }
}
