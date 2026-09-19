using System.Text.Json;
using Application.Features.Dispatch.Commands.SyncDispatche;
using Application.Features.Dispatch.Models;
using Application.Features.Dispatch.Options;
using Application.Features.Dispatch.Queries;
using Application.Features.Fleet.Commands.SyncFleet;
using Application.Features.Fleet.Interfaces;
using Application.Features.Fleet.Models;
using Application.Features.Fleet.Queries.GetFleetLocations;
using Application.Features.Fleet.Services;
using Application.Features.Fuel.Services;
using Application.Features.Routing.Background;
using Application.Features.Routing.Commands;
using Application.Features.Routing.Exceptions;
using Application.Features.Routing.Services.FuelPlanning;
using Application.Features.Synchronization.Interfaces;
using Application.Features.Synchronization.Models;
using Application.Features.Synchronization.Options;
using Application.Features.Synchronization.Services;
using Application.Interfaces;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Application.Features.Synchronization.Services;

public sealed class FleetSynchronizationOperation(
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
    while (!stoppingToken.IsCancellationRequested)
    {
      try
      {
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

  private async Task DataLoopAsync(
    TaskCompletionSource catalogsReady,
    CancellationToken ct
  )
  {
    while (true)
    {
      await RunJobAsync(
        "catalog",
        config.CatalogSeconds,
        async (services, token) =>
        {
          var result = await services
            .GetRequiredService<ISender>()
            .Send(new SyncFleetCommand(), token);
          if (!result.Success)
            throw new InvalidOperationException(
              "Fleet catalog synchronization failed."
            );
        },
        ct
      );
      await RunJobAsync(
        "assignments",
        config.AssignmentsSeconds,
        async (services, token) =>
        {
          var result = await services
            .GetRequiredService<ISender>()
            .Send(new SyncAssignmentsCommand(), token);
          if (!result.Success)
            throw new InvalidOperationException(
              "Fleet assignment synchronization failed."
            );
        },
        ct
      );
      catalogsReady.TrySetResult();
      await RunJobAsync(
        "fuel-exchange-rate",
        3600,
        async (services, token) =>
        {
          await services
            .GetRequiredService<FuelExchangeRateService>()
            .RefreshAsync(token);
        },
        ct
      );
      await Task.Delay(TimeSpan.FromSeconds(5), ct);
    }
  }

  private async Task DispatchLoopAsync(Task catalogsReady, CancellationToken ct)
  {
    await catalogsReady.WaitAsync(ct);
    while (true)
    {
      await RunJobAsync(
        "dispatch",
        config.DispatchSeconds,
        async (services, token) =>
        {
          var result = await services
            .GetRequiredService<ISender>()
            .Send(new SyncDispatchesCommand(), token);
          if (!result.Success)
            throw new InvalidOperationException(
              "Dispatch synchronization failed."
            );
          if (result.Response > 0)
            lock (stateGate)
            {
              state.LastPlanningScan = default;
            }
        },
        ct
      );
      await Task.Delay(TimeSpan.FromSeconds(5), ct);
    }
  }

  private async Task TelemetryLoopAsync(CancellationToken ct)
  {
    while (true)
    {
      await RunJobAsync(
        "telemetry",
        config.TelemetrySeconds,
        async (services, token) =>
        {
          var provider =
            services.GetRequiredService<IFleetTelemetryFeedProvider>();
          string? cursor;
          lock (stateGate)
            cursor = state.TelemetryCursor;
          var updates = new TelemetryFeedAccumulator(DateTime.UtcNow);
          var cursors = new HashSet<string>();
          while (true)
          {
            var feed = await provider.GetFeedAsync(cursor, token);
            updates.Append(feed);
            if (
              feed.HasNextPage
              && (feed.Cursor == cursor || !cursors.Add(feed.Cursor))
            )
              throw new InvalidOperationException(
                "Samsara returned a repeated feed cursor."
              );
            cursor = feed.Cursor;
            if (!feed.HasNextPage)
              break;
            await Task.Delay(250, token);
          }
          await PublishAsync(
            updates.Points.ToArray(),
            token,
            feed: updates,
            cursor: cursor
          );
        },
        ct
      );
      await Task.Delay(TimeSpan.FromSeconds(1), ct);
    }
  }

  private async Task LocationLoopAsync(CancellationToken ct)
  {
    if (!config.HighFrequencyLocations)
    {
      await Task.Delay(Timeout.Infinite, ct);
      return;
    }
    while (true)
    {
      await RunJobAsync(
        "locations",
        config.TelemetrySeconds,
        (_, token) => PublishAsync([], token, true),
        ct
      );
      await Task.Delay(TimeSpan.FromSeconds(1), ct);
    }
  }

  private async Task PublishAsync(
    IReadOnlyList<VehicleLocationPoint> updates,
    CancellationToken ct,
    bool highFrequency = false,
    TelemetryFeedAccumulator? feed = null,
    string? cursor = null
  )
  {
    using var scope = scopes.CreateScope();
    var services = scope.ServiceProvider;
    var fleet = await services
      .GetRequiredService<FleetCache>()
      .GetAsync(services.GetRequiredService<IAppDbContext>(), ct);
    IReadOnlyList<TruckLocation> stream = highFrequency
      ? await services
        .GetRequiredService<FleetLocationStream>()
        .GetAsync(
          services.GetRequiredService<IFleetTelemetryProvider>(),
          fleet,
          ct
        )
      : [];
    await publishGate.WaitAsync(ct);
    try
    {
      // Do not checkpoint a feed cursor before its snapshot can be published.
      if (feed is not null)
        lock (stateGate)
          state.Apply(feed.Updates, cursor!);
      PublishSnapshot(updates, fleet, stream);
    }
    finally
    {
      publishGate.Release();
    }
  }

  private void PublishSnapshot(
    IReadOnlyList<VehicleLocationPoint> updates,
    IReadOnlyList<FleetTruckInfo> fleet,
    IReadOnlyList<TruckLocation> stream
  )
  {
    // Followers must receive stream positions even if the feed is unavailable.
    lock (stateGate)
      state.ApplyLocations(
        stream.Select(point => new VehicleLocationPoint
        {
          ExternalId = point.TruckExternalId,
          Latitude = point.Latitude,
          Longitude = point.Longitude,
          Speed = point.Speed,
          Heading = point.Heading,
          UpdatedAt = point.UpdatedAt,
          FormattedLocation = point.FormattedLocation,
        })
      );
    List<VehicleTelemetry> vehicles;
    var observedAt = DateTime.UtcNow;
    lock (stateGate)
      vehicles = state
        .Vehicles.Values.Select(x => new VehicleTelemetry
        {
          ExternalId = x.ExternalId,
          Latitude = x.Latitude,
          Longitude = x.Longitude,
          Speed = x.Speed,
          Heading = x.Heading,
          UpdatedAt = x.UpdatedAt,
          ObservedAt = observedAt,
          FormattedLocation = x.FormattedLocation,
          EngineState = x.EngineState,
          FuelPercent = x.FuelPercent,
          FuelUpdatedAt = x.FuelUpdatedAt,
          OutsideTemperatureCelsius = x.OutsideTemperatureCelsius,
          OutsideTemperatureUpdatedAt = x.OutsideTemperatureUpdatedAt,
        })
        .ToList();
    var active = fleet
      .Where(x => x.IsActive)
      .ToDictionary(x => x.TruckExternalId);
    TruckLocation Map(FleetTruckInfo truck, VehicleLocationPoint location) =>
      new()
      {
        TruckId = truck.TruckId,
        TruckExternalId = truck.TruckExternalId,
        UnitNumber = truck.UnitNumber,
        DriverName = truck.DriverName,
        TrailerNumber = truck.TrailerNumber,
        Latitude = location.Latitude,
        Longitude = location.Longitude,
        Speed = location.Speed,
        Heading = location.Heading,
        UpdatedAt = location.UpdatedAt,
        ObservedAt = observedAt,
        FormattedLocation = location.FormattedLocation,
      };
    var trucks = vehicles
      .Where(x => x.UpdatedAt != default && active.ContainsKey(x.ExternalId))
      .Select(x =>
      {
        var item = Map(
          active[x.ExternalId],
          new()
          {
            Latitude = x.Latitude,
            Longitude = x.Longitude,
            Speed = x.Speed,
            Heading = x.Heading,
            UpdatedAt = x.UpdatedAt,
            FormattedLocation = x.FormattedLocation,
          }
        );
        item.ObservedAt = x.ObservedAt;
        item.EngineState = x.EngineState;
        item.FuelPercent = x.FuelPercent;
        item.FuelUpdatedAt = x.FuelUpdatedAt;
        item.OutsideTemperatureCelsius = x.OutsideTemperatureCelsius;
        item.OutsideTemperatureUpdatedAt = x.OutsideTemperatureUpdatedAt;
        return item;
      })
      .ToList();
    var points = (telemetry.Current?.Points ?? [])
      .Concat(stream)
      .Concat(
        updates
          .Where(x => active.ContainsKey(x.ExternalId))
          .Select(x => Map(active[x.ExternalId], x))
      )
      .Where(x => x.UpdatedAt > DateTime.UtcNow.AddMinutes(-2))
      .DistinctBy(x => (x.TruckId, x.UpdatedAt))
      .OrderBy(x => x.UpdatedAt)
      .ToList();
    FleetLocationSnapshot.UpdateFromStream(
      trucks,
      (telemetry.Current?.Trucks ?? []).Concat(stream)
    );
    telemetry.Set(new() { Trucks = trucks, Points = points });
  }

  private async Task PlanningLoopAsync(CancellationToken ct)
  {
    while (true)
    {
      await RunJobAsync(
        "planning",
        config.PlanningSeconds,
        async (services, token) =>
        {
          var mediator = services.GetRequiredService<ISender>();
          var rows = new List<TruckDispatchBoardResponse>();
          for (var page = 1; ; page++)
          {
            var board = await mediator.Send(
              new GetDispatchBoardQuery(
                Page: page,
                PageSize: 100,
                IncludeHos: false,
                IncludeFinancials: false,
                IncludeEta: false,
                IdentitiesOnly: true
              ),
              token
            );
            if (!board.Success || board.Response is null)
              throw new InvalidOperationException(
                "Dispatch board is unavailable."
              );
            rows.AddRange(
              board.Response.Items.Where(x =>
                x.TruckId.HasValue && x.Dispatches.Count > 0
              )
            );
            if (!board.Response.HasNextPage)
              break;
          }
          List<Guid> selected;
          lock (stateGate)
          {
            var ids = rows.Select(x => x.TruckId!.Value).ToHashSet();
            state.PendingTrucks.RemoveAll(x => !ids.Contains(x));
            if (
              state.PendingTrucks.Count == 0
              || state.LastPlanningScan == default
            )
              foreach (var id in ids)
                if (!state.PendingTrucks.Contains(id))
                  state.PendingTrucks.Add(id);
            state.LastPlanningScan = DateTime.UtcNow;
            selected = state
              .PendingTrucks.Take(config.MaxTrucksPerPlanningCycle)
              .ToList();
          }
          foreach (var id in selected)
          {
            var row = rows.First(x => x.TruckId == id);
            await RunJobAsync(
              $"truck:{id}",
              config.PlanningSeconds,
              async (truckServices, truckToken) =>
              {
                var planner = truckServices.GetRequiredService<ISender>();
                var prepared = await planner.Send(
                  new PrepareTruckPlanningCommand(id),
                  truckToken
                );
                if (!prepared.Success || prepared.Response is null)
                  throw new InvalidOperationException(
                    "Route preparation will retry."
                  );
                var current = prepared.Response;
                if (current.State?.Plan is null && current.DispatchId.HasValue)
                  throw new InvalidOperationException(
                    "Route preparation will retry."
                  );
                var index = row.Dispatches.FindIndex(x =>
                  x.Id == current.DispatchId
                  && x.ExecutionLegId == current.ExecutionLegId
                );
                if (index >= 0)
                {
                  var preparation =
                    truckServices.GetRequiredService<RoutePreparationQueue>();
                  foreach (
                    var next in row
                      .Dispatches.Skip(index + 1)
                      .Where(x => !x.ExecutionLegId.HasValue)
                  )
                    preparation.Request(next.Id, id);
                }
                await RunJobAsync(
                  $"fuel:{id}",
                  config.PlanningSeconds,
                  (fuelServices, fuelToken) =>
                    fuelServices
                      .GetRequiredService<FuelPriceRefreshService>()
                      .RefreshAsync(current, fuelToken),
                  truckToken
                );
                if (index >= 0)
                  foreach (
                    var next in row
                      .Dispatches.Skip(index + 1)
                      .Take(config.UpcomingRoutesPerTruck)
                  )
                  {
                    var upcoming = await planner.Send(
                      new PrepareUpcomingPlanningCommand(
                        next.Id,
                        next.ExecutionLegId,
                        id
                      ),
                      truckToken
                    );
                    if (!upcoming.Success)
                      throw new InvalidOperationException(
                        "Upcoming route preparation will retry."
                      );
                  }
              },
              token
            );
            lock (stateGate)
              state.PendingTrucks.Remove(id);
          }
        },
        ct
      );
      await Task.Delay(TimeSpan.FromSeconds(1), ct);
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
    }
  }
}
