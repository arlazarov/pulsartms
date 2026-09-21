using System.Text.Json;
using Application.Diagnostics;
using Application.Features.Dispatch.Commands.SyncDispatche;
using Application.Features.Dispatch.Models;
using Application.Features.Dispatch.Options;
using Application.Features.Dispatch.Queries;
using Application.Features.Fleet.Commands.SyncFleet;
using Application.Features.Fleet.Interfaces;
using Application.Features.Fleet.Queries.GetFleetLocations;
using Application.Features.Fleet.Services;
using Application.Features.Fuel.Services;
using Application.Features.Routing.Background;
using Application.Features.Routing.Commands;
using Application.Features.Routing.Services.FuelPlanning;
using Application.Features.Synchronization.Interfaces;
using Application.Features.Synchronization.Models;
using Application.Features.Synchronization.Options;
using Application.Features.Synchronization.Services;
using Application.Interfaces;
using Domain.Rules;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Application.Features.Synchronization.Services;

public sealed partial class FleetSynchronizationOperation(
  IServiceScopeFactory scopes,
  IOptions<SynchronizationOptions> options,
  IOptions<DispatchImportOptions> imports,
  ServerTelemetry telemetry,
  ILogger<FleetSynchronizationOperation> logger
) : IFleetSynchronizationOperation, ISynchronizationStatusProvider
{
  private volatile bool active;
  public SynchronizationStatus Status
  {
    get
    {
      lock (stateGate)
        return new(
          config.Enabled,
          active,
          state.PendingTrucks.Count,
          state
            .Jobs.Where(x =>
              x.Key != "dispatch"
              || !string.IsNullOrWhiteSpace(imports.Value.Provider)
            )
            .ToDictionary(
              x => x.Key,
              x => new SynchronizationJobStatus(
                x.Value.LastSuccess,
                x.Value.NextRun,
                x.Value.Failures,
                x.Value.Error
              )
            )
        );
    }
  }
  private readonly string owner = Guid.NewGuid().ToString("N");
  private readonly object stateGate = new();
  private readonly SemaphoreSlim publishGate = new(1, 1);
  private SynchronizationState state = new();
  private readonly SynchronizationOptions config = options.Value;
  private static readonly JsonSerializerOptions Json = new(
    JsonSerializerDefaults.Web
  );

  public async Task RunAsync(CancellationToken stoppingToken)
  {
    if (!config.Enabled)
    {
      logger.LogInformation("Server synchronization is disabled.");
      return;
    }
    // An instance that stops synchronising is not alive in any useful sense,
    // and nothing noticed the last time it happened. Registering here means
    // only an instance actually told to synchronise is judged on it.
    BackgroundHeartbeat.Expect(
      "synchronization",
      TimeSpan.FromSeconds(
        Math.Max(config.CheckpointSeconds, config.RetrySeconds)
      )
    );
    while (!stoppingToken.IsCancellationRequested)
    {
      try
      {
        // The outer loop turning is itself a sign of life, including for an
        // instance that holds no lease and is only watching.
        BackgroundHeartbeat.Beat("synchronization");
        using var scope = scopes.CreateScope();
        var store =
          scope.ServiceProvider.GetRequiredService<ISynchronizationStore>();
        if (await store.AcquireAsync(owner, DateTime.UtcNow, stoppingToken))
        {
          state = await store.ReadAsync(stoppingToken);
          await PublishAsync([], stoppingToken);
          logger.LogInformation(
            "Server synchronization started with a database lease."
          );
          await RunOwnedAsync(stoppingToken);
        }
        else
        {
          state = await store.ReadAsync(stoppingToken);
          await PublishAsync([], stoppingToken);
        }
      }
      catch (OperationCanceledException)
        when (stoppingToken.IsCancellationRequested)
      {
        break;
      }
      catch (Exception ex)
      {
        logger.LogWarning(ex, "Synchronization will retry.");
      }
      try
      {
        await Task.Delay(
          TimeSpan.FromSeconds(config.RetrySeconds),
          stoppingToken
        );
      }
      catch (OperationCanceledException)
        when (stoppingToken.IsCancellationRequested)
      {
        break;
      }
    }
  }

  private async Task RunOwnedAsync(CancellationToken stoppingToken)
  {
    active = true;
    using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(
      stoppingToken
    );
    var ct = lifetime.Token;
    var catalogsReady = new TaskCompletionSource(
      TaskCreationOptions.RunContinuationsAsynchronously
    );
    var tasks = new List<Task>
    {
      LeaseLoopAsync(lifetime),
      DataLoopAsync(catalogsReady, ct),
      TelemetryLoopAsync(ct),
      LocationLoopAsync(ct),
      PlanningLoopAsync(ct),
      CheckpointLoopAsync(ct),
    };
    if (!string.IsNullOrWhiteSpace(imports.Value.Provider))
      tasks.Add(DispatchLoopAsync(catalogsReady.Task, ct));
    try
    {
      await Task.WhenAny(tasks);
    }
    finally
    {
      active = false;
      await lifetime.CancelAsync();
      try
      {
        await Task.WhenAll(tasks);
      }
      catch (OperationCanceledException) { }
      catch (Exception ex)
      {
        logger.LogWarning(ex, "Synchronization loop stopped.");
      }
      using var final = new CancellationTokenSource(TimeSpan.FromSeconds(5));
      try
      {
        using var scope = scopes.CreateScope();
        var store =
          scope.ServiceProvider.GetRequiredService<ISynchronizationStore>();
        await store.SaveAsync(owner, StateJson(), final.Token);
        await store.ReleaseAsync(owner, final.Token);
      }
      catch (Exception ex)
      {
        logger.LogWarning(ex, "Synchronization checkpoint shutdown failed");
      }
    }
  }

  private async Task LeaseLoopAsync(CancellationTokenSource lifetime)
  {
    try
    {
      while (true)
      {
        await Task.Delay(TimeSpan.FromSeconds(45), lifetime.Token);
        using var scope = scopes.CreateScope();
        if (
          !await scope
            .ServiceProvider.GetRequiredService<ISynchronizationStore>()
            .RenewAsync(owner, DateTime.UtcNow, lifetime.Token)
        )
          break;
      }
    }
    catch (OperationCanceledException) when (lifetime.IsCancellationRequested)
    { }
    catch (Exception ex)
    {
      logger.LogWarning(ex, "Synchronization lease renewal failed");
    }
    finally
    {
      await lifetime.CancelAsync();
    }
  }

  private async Task RunJobAsync(
    string name,
    int interval,
    Func<IServiceProvider, CancellationToken, Task> run,
    CancellationToken ct
  )
  {
    lock (stateGate)
    {
      if (!state.Jobs.TryGetValue(name, out var job))
        state.Jobs[name] = job = new();
      if (job.NextRun > DateTime.UtcNow)
        return;
    }
    using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
    timeout.CancelAfter(TimeSpan.FromSeconds(config.JobTimeoutSeconds));
    try
    {
      using var scope = scopes.CreateScope();
      await run(scope.ServiceProvider, timeout.Token);
      lock (stateGate)
        state.Jobs[name].Success(DateTime.UtcNow, interval);
    }
    catch (OperationCanceledException) when (ct.IsCancellationRequested)
    {
      throw;
    }
    catch (RoutePlanningException ex) when (ex.RetryAfter != DateTime.MaxValue)
    {
      var now = DateTime.UtcNow;
      lock (stateGate)
        state.Jobs[name].NextRun =
          ex.RetryAfter > now
            ? ex.RetryAfter
            : now.AddSeconds(config.RetrySeconds);
    }
    catch (Exception ex)
    {
      lock (stateGate)
        state
          .Jobs[name]
          .Fail(DateTime.UtcNow, config.RetrySeconds, ex.GetType().Name);
      logger.LogWarning(
        ex,
        "Synchronization job {Job} failed with status {StatusCode}; "
          + "retaining saved data and retrying.",
        name,
        (ex as HttpRequestException)?.StatusCode
      );
    }
  }

  private string StateJson()
  {
    lock (stateGate)
      return JsonSerializer.Serialize(state, Json);
  }

  private async Task CheckpointLoopAsync(CancellationToken ct)
  {
    while (true)
    {
      await Task.Delay(TimeSpan.FromSeconds(config.CheckpointSeconds), ct);
      using var scope = scopes.CreateScope();
      await scope
        .ServiceProvider.GetRequiredService<ISynchronizationStore>()
        .SaveAsync(owner, StateJson(), ct);
      // The owning instance does its work inside RunOwnedAsync, which does
      // not return for hours, so the outer loop cannot report for it.
      BackgroundHeartbeat.Beat("synchronization");
    }
  }
}
