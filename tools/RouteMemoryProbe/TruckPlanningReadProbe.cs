using System.Data;
using System.Text.Json;
using Application;
using Application.Features.Dispatch.Queries;
using Application.Features.Eta.Options;
using Application.Features.Execution.Models;
using Application.Features.Fuel.Services;
using Application.Features.Routing.Algorithms;
using Application.Features.Routing.Interfaces;
using Application.Features.Routing.Models;
using Application.Features.Routing.Options;
using Application.Features.Routing.Services.Deadheads;
using Application.Features.Routing.Services.Routes;
using Application.Features.Synchronization.Options;
using Infrastructure;
using Infrastructure.Persistence;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http;
using Microsoft.Extensions.Options;
using Npgsql;

internal static class TruckPlanningReadProbe
{
  public static async Task RunAsync(
    string truckNumber,
    bool candidatesOnly = false,
    bool nativeRoadIntegrity = false
  )
  {
    if (
      !nativeRoadIntegrity
      && (string.IsNullOrWhiteSpace(truckNumber) || truckNumber.Length > 40)
    )
      throw new ArgumentException("Supply one truck unit number.");
    var config = new ConfigurationBuilder()
      .SetBasePath(Path.GetFullPath("Server/API"))
      .AddJsonFile("appsettings.json")
      .AddJsonFile("appsettings.Development.json")
      .AddUserSecrets("pulsartms-api-local")
      .AddEnvironmentVariables()
      .Build();
    using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(1));
    var ct = timeout.Token;
    await using var connection = new NpgsqlConnection(
      config.GetConnectionString("DefaultConnection")
    );
    await connection.OpenAsync(ct);
    await using var transaction = await connection.BeginTransactionAsync(
      IsolationLevel.RepeatableRead,
      ct
    );
    await using (
      var settings = new NpgsqlCommand(
        "SET TRANSACTION READ ONLY; SET LOCAL statement_timeout='10s'",
        connection,
        transaction
      )
    )
      await settings.ExecuteNonQueryAsync(ct);
    var services = new ServiceCollection();
    services.AddSingleton<IConfiguration>(config);
    services.AddLogging();
    services.AddApplication();
    services.AddInfrastructure(config);
    var outbound = new ProviderGuard();
    services.AddSingleton<IHttpMessageHandlerBuilderFilter>(outbound);
    services.AddScoped(_ =>
    {
      var db = new AppDbContext(
        new DbContextOptionsBuilder<AppDbContext>()
          .UseNpgsql(connection)
          .Options
      );
      db.Database.UseTransaction(transaction);
      return db;
    });
    Bind<SynchronizationOptions>("Synchronization");
    Bind<FuelRegionOptions>("FuelRegions");
    Bind<RouteRecalculationBudgetOptions>("RouteRecalculationBudget");
    Bind<RoutePreparationOptions>("RoutePreparation");
    Bind<EtaPlanningOptions>("EtaPlanning");
    await using var provider = services.BuildServiceProvider();
    await using var scope = provider.CreateAsyncScope();
    var scoped = scope.ServiceProvider;
    var db = scoped.GetRequiredService<AppDbContext>();
    if (nativeRoadIntegrity)
    {
      await InspectNativeRoadsAsync(db, scoped, ct);
      if (outbound.Attempts != 0)
        throw new InvalidOperationException("Provider attempt was blocked.");
      await transaction.RollbackAsync(ct);
      return;
    }
    var truckId = await db
      .Trucks.AsNoTracking()
      .Where(x => x.UnitNumber == truckNumber)
      .Select(x => x.Id)
      .SingleAsync(ct);
    var board = await scoped
      .GetRequiredService<ISender>()
      .Send(
        new GetDispatchBoardQuery(
          TruckId: truckId,
          IncludeHos: false,
          IncludeFinancials: false,
          IncludeEta: false
        ),
        ct
      );
    if (!board.Success || board.Response is null)
      throw new InvalidOperationException("Board read did not succeed.");
    var loads =
      board
        .Response.Items.SingleOrDefault(x => x.TruckId == truckId)
        ?.Dispatches ?? [];
    var current = loads.FirstOrDefault();
    var preview = await scoped
      .GetRequiredService<RoutePreviewService>()
      .ForTruckAsync(truckId, ct);
    if (candidatesOnly)
    {
      await FuelCandidateDiagnosis.RunAsync(
        truckNumber,
        scoped,
        preview.State
          ?? throw new InvalidOperationException("No saved planning state."),
        loads,
        ct
      );
      if (outbound.Attempts != 0)
        throw new InvalidOperationException("Provider attempt was blocked.");
      await transaction.RollbackAsync(ct);
      return;
    }
    var saved = current is null
      ? null
      : await scoped
        .GetRequiredService<RouteDisplayCache>()
        .GetAsync(
          current.Id,
          () =>
            db
              .DispatchRoutePlans.AsNoTracking()
              .SingleOrDefaultAsync(
                x =>
                  x.DispatchId == current.Id
                  && x.ExecutionLegId == current.ExecutionLegId,
                ct
              ),
          ct,
          current.ExecutionLegId
        );
    var storedPlan = saved?.ReadMetadata();
    var currentId = current?.Id;
    var sourceSchedule = await db
      .DispatchStops.AsNoTracking()
      .Where(stop => stop.DispatchId == currentId)
      .Select(stop => new
      {
        stop.Id,
        stop.Sequence,
        stop.ScheduledDate,
        stop.ScheduledTime,
        stop.ScheduledDate2,
        stop.ScheduledTime2,
        stop.ArrivedAt,
      })
      .ToListAsync(ct);
    var fuel = await scoped
      .GetRequiredService<ITruckFuelPlanStore>()
      .ReadAsync(truckId, includeRoute: false, ct);
    var ids = loads
      .Where(x => !x.ExecutionLegId.HasValue)
      .Select(x => x.Id)
      .ToArray();
    var rawHistory = await scoped
      .GetRequiredService<IDeadheadHistoryReader>()
      .ReadAsync(ids, ct);
    var history = await scoped
      .GetRequiredService<DeadheadHistoryService>()
      .ReadAsync(ids, ct);
    var connections = await db
      .DispatchDeadheads.AsNoTracking()
      .Where(x => ids.Contains(x.DispatchId) && x.ExecutionLegId == null)
      .ToDictionaryAsync(x => x.DispatchId, ct);
    var profile = await scoped
      .GetRequiredService<RoutePlanningService>()
      .ProfileAsync(truckId, ct);
    var exchangeRate = await scoped
      .GetRequiredService<FuelExchangeRateService>()
      .ReadAsync(ct);
    if (outbound.Attempts != 0)
      throw new InvalidOperationException("Provider-free read attempted HTTP.");
    Console.WriteLine(
      JsonSerializer.Serialize(
        new
        {
          truckNumber,
          truckId,
          observedAt = DateTime.UtcNow,
          readOnly = true,
          providerRequests = outbound.Attempts,
          exchangeRate,
          sourceSchedule,
          connections = history.Values.Select(snapshot =>
          {
            var pair = DeadheadConnection.Find(snapshot);
            var entry = connections.GetValueOrDefault(snapshot.Current.Id);
            object Summary(RouteWorkSnapshot load) =>
              new
              {
                load.Id,
                load.TruckId,
                load.ExecutionLegId,
                load.AssignmentRevision,
                load.ExecutionStatus,
                load.ShipDate,
                load.DeliveryDate,
                stops = load.Stops.Select(stop => new
                {
                  stop.Id,
                  stop.Sequence,
                  stop.Job,
                  stop.ManualAction,
                  stop.TruckId,
                  stop.ScheduledDate,
                  stop.ScheduledTime,
                  stop.Latitude,
                  stop.Longitude,
                }),
              };
            return new
            {
              current = Summary(snapshot.Current),
              snapshot.HasUnknownStart,
              raw = rawHistory
                .GetValueOrDefault(snapshot.Current.Id)
                ?.Predecessors.Select(Summary),
              resolved = snapshot.Predecessors.Select(Summary),
              pairPrevious = pair?.Previous.Id,
              pairPreviousLeg = pair?.Previous.ExecutionLegId,
              expectedHash = pair?.Signature(profile),
              saved = entry is null
                ? null
                : new
                {
                  entry.PreviousDispatchId,
                  entry.PreviousExecutionLegId,
                  entry.InputHash,
                  entry.Miles,
                  entry.CalculatedAt,
                  entry.RetryAfter,
                  entry.ErrorMessage,
                  hasRoute = entry.RouteJson is not null,
                  accepted = pair?.ReadRoute(entry, profile) is not null,
                },
            };
          }),
          loads = loads.Select(load => new
          {
            dispatchId = load.Id,
            load.LoadNumber,
            load.Status,
            load.ExecutionLegId,
            load.AssignmentRevision,
            load.ExecutionStatus,
            load.AwaitingReceipt,
            stopCount = load.Stops.Count,
            stops = load.Stops.Select(stop => new
            {
              stop.Id,
              stop.Sequence,
              stop.Job,
              stop.IsCompleted,
              stop.ScheduledDate,
              stop.ScheduledTime,
              stop.ScheduledDate2,
              stop.ScheduledTime2,
            }),
          }),
          preview = new
          {
            preview.DispatchId,
            preview.ExecutionLegId,
            preview.AssignmentRevision,
            preview.Message,
            hasAcceptedRoute = preview.State?.Plan is not null,
            version = preview.State?.Plan?.Version,
            inputsChanged = preview.State?.Plan?.InputsChanged,
            nextStopId = preview.State?.Plan?.Tracking.NextStopId,
          },
          storedCurrentRoute = storedPlan is null
            ? null
            : new
            {
              storedPlan.DispatchId,
              storedPlan.ExecutionLegId,
              storedPlan.AssignmentRevision,
              storedPlan.TruckId,
              storedPlan.Version,
              storedPlan.CalculatedAt,
              persistedInputsChanged = storedPlan.InputsChanged,
              storedPlan.Tracking.NextStopId,
              storedPlan.Tracking.AllStopsPassed,
              stopIds = storedPlan.Stops.Select(stop => stop.Id),
              stopCount = storedPlan.Stops.Count,
            },
          savedFuel = fuel is null
            ? null
            : new
            {
              fuel.RootDispatchId,
              fuel.RootExecutionLegId,
              fuel.AssignmentRevision,
              fuel.CalculatedAt,
              fuel.Plan.PricingDate,
              fuel.Plan.PriceDates,
              fuel.Plan.ManuallyEdited,
              fuel.Plan.ManualStartingFuel,
              fuel.Plan.SelectionVersion,
              probeSelectionVersion = FuelOptimizer.SelectionVersion,
              fuel.Plan.EstimatedStationAccess,
              fuel.Plan.ReusedCheckedRoute,
              fuel.Plan.StartAccessMiles,
              automaticRefreshBlockedByManual = fuel.Plan.ManuallyEdited
                || fuel.Plan.ManualStartingFuel,
              assignmentsMatch = current is not null
                && FuelPlanProjection.AssignmentsMatch(
                  fuel.Plan,
                  current.Id,
                  loads
                ),
              policySignatureMatches = fuel.Plan.ArrivalPolicy?.PolicySignature
                == scoped
                  .GetRequiredService<IOptions<FuelRegionOptions>>()
                  .Value.Signature,
              arrivalPolicy = fuel.Plan.ArrivalPolicy is { } policy
                ? new
                {
                  policy.MinimumGallons,
                  policy.TargetGallons,
                  policy.EconomicPurchasesOnly,
                  policy.EscapeMiles,
                  policy.NextDispatchId,
                }
                : null,
              persistedNeedsRefresh = fuel.Plan.NeedsRefresh,
              persistedRefreshReasons = fuel.Plan.RefreshReasons,
              fuel.Plan.RouteVersion,
              fuel.Plan.StartingGallons,
              fuel.Plan.RemainingMiles,
              fuel.Plan.PurchaseCostUsd,
              fuel.Plan.ExpectedFutureFuelCostUsd,
              fuel.Plan.RouteChecks,
              itinerary = fuel.Stops.Select(stop => new
              {
                stop.DispatchId,
                stop.ExecutionLegId,
                stop.AssignmentRevision,
                stopId = stop.Stop.Id,
              }),
              arrivalCount = fuel.Plan.StopArrivals.Count,
              arrivalStopIds = fuel.Plan.StopArrivals.Select(stop => new
              {
                stop.DispatchId,
                stop.StopId,
                stop.Gallons,
                stop.Percent,
              }),
              purchases = fuel.Plan.Stops.Select(stop => new
              {
                stop.StationId,
                stop.Name,
                stop.EconomicUsdPerGallon,
                stop.RouteMilesAhead,
                stop.ArrivalGallons,
                stop.DepartureGallons,
                stop.BeforeStopId,
                stop.Country,
                stop.PriceDate,
                stop.PriceEstimated,
                stop.EstimatedArrival,
                stop.BuyGallons,
                stop.DetourMiles,
              }),
            },
        }
      )
    );
    await transaction.RollbackAsync(ct);

    void Bind<T>(string section)
      where T : class =>
      services
        .AddOptions<T>()
        .Bind(config.GetSection(section))
        .ValidateDataAnnotations();
  }

  private static async Task InspectNativeRoadsAsync(
    AppDbContext db,
    IServiceProvider services,
    CancellationToken ct
  )
  {
    var legs = await db
      .ExecutionLegs.AsNoTracking()
      .Include(x => x.Loads)
      .ToListAsync(ct);
    var results = new List<object>();
    foreach (var leg in legs)
    {
      var stops = ExecutionStopRows.Read(leg);
      var saved = await db
        .DispatchBaseRoutes.AsNoTracking()
        .SingleOrDefaultAsync(x => x.ExecutionLegId == leg.Id, ct);
      if (saved is null)
      {
        results.Add(new { leg.Status, hasRoad = false });
        continue;
      }
      var load = await db
        .Dispatches.AsNoTracking()
        .SingleAsync(x => x.Id == saved.DispatchId, ct);
      var profile = await services
        .GetRequiredService<TruckPlanningProfileService>()
        .GetUncachedAsync(leg.TruckId, ct);
      var hash = BaseRouteService.Signature(
        RouteWorkProjection.Capture(load, leg, stops),
        profile
      );
      var route = SavedRouteReader.Route(saved.RouteJson, stops.Count - 1);
      var points = stops
        .Select(x => new RoutePoint(
          (double)(x.Latitude ?? decimal.MinValue),
          (double)(x.Longitude ?? decimal.MinValue)
        ))
        .ToArray();
      results.Add(
        new
        {
          leg.Status,
          hasRoad = true,
          underReview = leg.SourceReviewReason is not null,
          linkedLoad = leg.Loads.Any(x => x.DispatchId == load.Id),
          stopCount = stops.Count,
          signatureMatches = saved.InputHash == hash,
          validRoute = route is not null,
          anchorsMatch = RouteAnchoring.Matches(route, points),
          validMiles = route?.Legs.All(x =>
            double.IsFinite(x.Miles) && x.Miles is >= 0 and <= 1_000_000
          ),
        }
      );
    }
    Console.WriteLine(
      JsonSerializer.Serialize(new { readOnly = true, roads = results })
    );
  }

  private sealed class ProviderGuard : IHttpMessageHandlerBuilderFilter
  {
    public int Attempts;

    public Action<HttpMessageHandlerBuilder> Configure(
      Action<HttpMessageHandlerBuilder> next
    ) =>
      builder =>
      {
        next(builder);
        builder.PrimaryHandler = new RejectHttp(this);
      };

    private sealed class RejectHttp(ProviderGuard owner) : HttpMessageHandler
    {
      protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken
      )
      {
        Interlocked.Increment(ref owner.Attempts);
        throw new InvalidOperationException(
          "Provider HTTP is forbidden in the read-only planning probe."
        );
      }
    }
  }
}
