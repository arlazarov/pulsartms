using System.Diagnostics;
using System.Text.Json;
using Application;
using Application.Features.Eta.Options;
using Application.Features.Routing.Exceptions;
using Application.Features.Routing.Interfaces;
using Application.Features.Routing.Options;
using Application.Features.Routing.Services.Routes;
using Application.Features.Synchronization.Options;
using Infrastructure;
using Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

internal static class TruckFuelRefresh
{
  public static async Task RunAsync(
    string truckNumber,
    bool initializationOnly = false,
    bool allowInitialCalculation = false
  )
  {
    if (string.IsNullOrWhiteSpace(truckNumber) || truckNumber.Length > 40)
      throw new ArgumentException("Supply one truck unit number.");
    var config = new ConfigurationBuilder()
      .SetBasePath(Path.GetFullPath("Server/API"))
      .AddJsonFile("appsettings.json")
      .AddJsonFile("appsettings.Development.json")
      .AddUserSecrets("pulsartms-api-local")
      .AddEnvironmentVariables()
      .Build();
    var services = new ServiceCollection();
    services.AddSingleton<IConfiguration>(config);
    services.AddLogging();
    services.AddApplication();
    services.AddInfrastructure(config);
    Bind<SynchronizationOptions>("Synchronization");
    // This standalone process does not host the telemetry worker.
    services.Configure<SynchronizationOptions>(options =>
      options.Enabled = false
    );
    Bind<FuelRegionOptions>("FuelRegions");
    Bind<RouteRecalculationBudgetOptions>("RouteRecalculationBudget");
    Bind<RoutePreparationOptions>("RoutePreparation");
    Bind<EtaPlanningOptions>("EtaPlanning");
    await using var provider = services.BuildServiceProvider();
    await using var scope = provider.CreateAsyncScope();
    var scoped = scope.ServiceProvider;
    using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(2));
    var ct = timeout.Token;
    Guid? truckId = null;
    Guid? dispatchId = null;
    Guid? executionLegId = null;
    long? assignmentRevision = null;
    DateTime? expectedCalculatedAt = null;
    var stage = "resolve-db-context";
    try
    {
      var db = scoped.GetRequiredService<AppDbContext>();
      stage = "open-read-only-transaction";
      await using var transaction = initializationOnly
        ? await db.Database.BeginTransactionAsync(ct)
        : null;
      if (initializationOnly)
        await db.Database.ExecuteSqlRawAsync(
          "SET TRANSACTION READ ONLY; SET LOCAL statement_timeout='10s'",
          ct
        );
      stage = "lookup-truck";
      truckId = await db
        .Trucks.AsNoTracking()
        .Where(x => x.UnitNumber == truckNumber)
        .Select(x => x.Id)
        .SingleAsync(ct);
      if (initializationOnly)
      {
        Console.WriteLine(
          JsonSerializer.Serialize(
            new
            {
              truckNumber,
              truckId,
              success = true,
              initializationOnly,
            }
          )
        );
        return;
      }
      stage = "read-route-preview";
      var preview = await scoped
        .GetRequiredService<RoutePreviewService>()
        .ForTruckAsync(truckId.Value, ct);
      if (
        preview.DispatchId is null
        || preview.State?.Plan
          is not { InputsChanged: false, Tracking.AllStopsPassed: false }
      )
        throw new RoutePlanningException(
          "The current dispatch needs a matching saved route; fuel unchanged."
        );
      dispatchId = preview.DispatchId;
      executionLegId = preview.ExecutionLegId;
      assignmentRevision = preview.AssignmentRevision;
      stage = "read-saved-fuel";
      var store = scoped.GetRequiredService<ITruckFuelPlanStore>();
      var saved = await store.ReadAsync(truckId.Value, false, ct);
      if (saved is null && !allowInitialCalculation)
        throw new RoutePlanningException(
          "No saved fuel revision was found; automatic refresh not started."
        );
      expectedCalculatedAt = saved?.CalculatedAt;
      if (
        saved?.Plan is { } previous
        && (previous.ManuallyEdited || previous.ManualStartingFuel)
      )
        throw new RoutePlanningException(
          "Manual fuel choices are protected; automatic refresh not started."
        );
      stage = "recalculate-fuel";
      var result = await scoped
        .GetRequiredService<AutomaticPlanningService>()
        .RecalculateFuelAsync(
          dispatchId.Value,
          ct,
          executionLegId,
          assignmentRevision,
          automaticRefreshRevision: expectedCalculatedAt
        );
      stage = "read-updated-fuel";
      var updated = await store.ReadAsync(truckId.Value, false, ct);
      var success =
        updated is not null
        && (
          expectedCalculatedAt is null
          || updated.CalculatedAt > expectedCalculatedAt
        )
        && updated.RootDispatchId == dispatchId
        && updated.RootExecutionLegId == executionLegId
        && updated.AssignmentRevision == assignmentRevision;
      Console.WriteLine(
        JsonSerializer.Serialize(
          new
          {
            truckNumber,
            truckId,
            dispatchId,
            executionLegId,
            assignmentRevision,
            success,
            expectedCalculatedAt,
            calculatedAt = updated?.CalculatedAt,
            result.Message,
          }
        )
      );
      if (!success)
        Environment.ExitCode = 1;
    }
    catch (Exception error)
    {
      Console.WriteLine(
        JsonSerializer.Serialize(
          new
          {
            truckNumber,
            truckId,
            dispatchId,
            executionLegId,
            assignmentRevision,
            success = false,
            initializationOnly,
            stage,
            expectedCalculatedAt,
            errorType = error.GetType().Name,
            diagnostic = Describe(error),
            message = initializationOnly
              ? "Read-only fuel initialization failed."
            : error
              is RoutePlanningException
                or PlanningSettingsConflictException
              ? error.Message
            : error is OperationCanceledException
              ? "Fuel refresh timed out; no reset was attempted."
            : "Fuel refresh failed; inspect safe server diagnostics.",
          }
        )
      );
      Environment.ExitCode = 1;
    }

    void Bind<T>(string section)
      where T : class =>
      services
        .AddOptions<T>()
        .Bind(config.GetSection(section))
        .ValidateDataAnnotations();
  }

  private static object[] Describe(Exception error)
  {
    var result = new List<object>();
    for (
      Exception? current = error;
      current is not null && result.Count < 6;
      current = current.InnerException
    )
      result.Add(
        new
        {
          type = current.GetType().FullName,
          frames = new StackTrace(current, false)
            .GetFrames()
            .Take(12)
            .Select(frame => new
            {
              type = frame.GetMethod()?.DeclaringType?.FullName,
              method = frame.GetMethod()?.Name,
            }),
        }
      );
    return result.ToArray();
  }
}
