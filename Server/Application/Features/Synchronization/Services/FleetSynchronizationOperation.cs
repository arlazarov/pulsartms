using Application.Features.Fleet.Services;
using Application.Features.Synchronization.Options;
using Application.Features.Synchronization.Services;
using Application.Features.Synchronization.Interfaces;
using Application.Features.Synchronization.Models;
using Application.Features.Routing.Commands;
using System.Text.Json;
using Application.Features.Dispatch.Commands.SyncDispatche;
using Application.Features.Dispatch.Queries;
using Application.Features.Fleet.Commands.SyncFleet;
using Application.Features.Fleet.Interfaces;
using Application.Features.Fleet.Models;
using Application.Features.Fleet.Queries.GetFleetLocations;
using Application.Interfaces;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Application.Features.Synchronization.Services;

public sealed class FleetSynchronizationOperation(IServiceScopeFactory scopes, IOptions<SynchronizationOptions> options,
  ServerTelemetry telemetry, ILogger<FleetSynchronizationOperation> logger) : IFleetSynchronizationOperation, ISynchronizationStatusProvider
{
  private volatile bool active;
  public SynchronizationStatus Status
  {
    get
    {
      lock (stateGate)
        return new(config.Enabled, active, state.PendingTrucks.Count,
          state.Jobs.ToDictionary(x => x.Key, x => new SynchronizationJobStatus(x.Value.LastSuccess, x.Value.NextRun, x.Value.Failures, x.Value.Error)));
    }
  }
  private readonly string owner = Guid.NewGuid().ToString("N");
  private readonly object stateGate = new();
  private SynchronizationState state = new();
  private readonly SynchronizationOptions config = options.Value;
  private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

  public async Task RunAsync(CancellationToken stoppingToken)
  {
    if (!config.Enabled) { logger.LogInformation("Server synchronization is disabled."); return; }
    while (!stoppingToken.IsCancellationRequested)
    {
      try
      {
        using var scope = scopes.CreateScope();
        var store = scope.ServiceProvider.GetRequiredService<ISynchronizationStore>();
        if (await store.AcquireAsync(owner, DateTime.UtcNow, stoppingToken))
        {
          state = await store.ReadAsync(stoppingToken);
          await PublishAsync([], stoppingToken);
          logger.LogInformation("Server synchronization started with a database lease.");
          await RunOwnedAsync(stoppingToken);
        }
        else
        {
          state = await store.ReadAsync(stoppingToken);
          await PublishAsync([], stoppingToken);
        }
      }
      catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
      catch (Exception ex) { logger.LogWarning(ex, "Synchronization will retry."); }
      try { await Task.Delay(TimeSpan.FromSeconds(config.RetrySeconds), stoppingToken); }
      catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
    }
  }

  private async Task RunOwnedAsync(CancellationToken stoppingToken)
  {
    active = true;
    using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
    var ct = lifetime.Token;
    var tasks = new[] { LeaseLoopAsync(lifetime), DataLoopAsync(ct), TelemetryLoopAsync(ct), PlanningLoopAsync(ct), CheckpointLoopAsync(ct) };
    try { await Task.WhenAny(tasks); }
    finally
    {
      active = false;
      await lifetime.CancelAsync();
      try { await Task.WhenAll(tasks); }
      catch (OperationCanceledException) { }
      catch (Exception ex) { logger.LogWarning(ex, "Synchronization loop stopped."); }
      using var final = new CancellationTokenSource(TimeSpan.FromSeconds(5));
      try
      {
        using var scope = scopes.CreateScope();
        var store = scope.ServiceProvider.GetRequiredService<ISynchronizationStore>();
        await store.SaveAsync(owner, StateJson(), final.Token);
        await store.ReleaseAsync(owner, final.Token);
      }
      catch (Exception ex) { logger.LogWarning(ex, "Synchronization checkpoint shutdown failed"); }
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
        if (!await scope.ServiceProvider.GetRequiredService<ISynchronizationStore>().RenewAsync(owner, DateTime.UtcNow, lifetime.Token))
          break;
      }
    }
    catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { }
    catch (Exception ex) { logger.LogWarning(ex, "Synchronization lease renewal failed"); }
    finally { await lifetime.CancelAsync(); }
  }

  private async Task DataLoopAsync(CancellationToken ct)
  {
    while (true)
    {
      await RunJobAsync("catalog", config.CatalogSeconds, async (services, token) =>
      {
        var result = await services.GetRequiredService<ISender>().Send(new SyncFleetCommand(), token);
        if (!result.Success) throw new InvalidOperationException("Fleet catalog synchronization failed.");
      }, ct);
      await RunJobAsync("assignments", config.AssignmentsSeconds, async (services, token) =>
      {
        var result = await services.GetRequiredService<ISender>().Send(new SyncAssignmentsCommand(), token);
        if (!result.Success) throw new InvalidOperationException("Fleet assignment synchronization failed.");
      }, ct);
      await RunJobAsync("dispatch", config.DispatchSeconds, async (services, token) =>
      {
        var result = await services.GetRequiredService<ISender>().Send(new SyncDispatchesCommand(), token);
        if (!result.Success) throw new InvalidOperationException("Dispatch synchronization failed.");
        if (result.Response > 0) lock (stateGate) { state.LastPlanningScan = default; }
      }, ct);
      await Task.Delay(TimeSpan.FromSeconds(5), ct);
    }
  }

  private async Task TelemetryLoopAsync(CancellationToken ct)
  {
    while (true)
    {
      await RunJobAsync("telemetry", config.TelemetrySeconds, async (services, token) =>
      {
        var provider = services.GetRequiredService<IFleetTelemetryFeedProvider>();
        string? cursor;
        lock (stateGate) cursor = state.TelemetryCursor;
        var updates = new List<TelemetryUpdate>();
        var cursors = new HashSet<string>();
        while (true)
        {
          var feed = await provider.GetFeedAsync(cursor, token);
          updates.AddRange(feed.Updates);
          if (feed.HasNextPage && (feed.Cursor == cursor || !cursors.Add(feed.Cursor)))
            throw new InvalidOperationException("Samsara returned a repeated feed cursor.");
          cursor = feed.Cursor;
          if (!feed.HasNextPage) break;
          await Task.Delay(250, token);
        }
        lock (stateGate) state.Apply(updates, cursor!);
        await PublishAsync(updates.Where(x => x.Gps is not null).Select(x => x.Gps!).ToList(), token, config.HighFrequencyLocations);
      }, ct);
      await Task.Delay(TimeSpan.FromSeconds(1), ct);
    }
  }

  private async Task PublishAsync(IReadOnlyList<VehicleLocationPoint> updates, CancellationToken ct, bool highFrequency = false)
  {
    using var scope = scopes.CreateScope();
    var services = scope.ServiceProvider;
    var fleet = await services.GetRequiredService<FleetCache>().GetAsync(services.GetRequiredService<IAppDbContext>(), ct);
    List<VehicleTelemetry> vehicles;
    var observedAt = DateTime.UtcNow;
    lock (stateGate) vehicles = state.Vehicles.Values.Select(x => new VehicleTelemetry
    { ExternalId = x.ExternalId, Latitude = x.Latitude, Longitude = x.Longitude, Speed = x.Speed, Heading = x.Heading,
      UpdatedAt = x.UpdatedAt, ObservedAt = observedAt, FormattedLocation = x.FormattedLocation, EngineState = x.EngineState,
      FuelPercent = x.FuelPercent, FuelUpdatedAt = x.FuelUpdatedAt }).ToList();
    var active = fleet.Where(x => x.IsActive).ToDictionary(x => x.TruckExternalId);
    TruckLocation Map(FleetTruckInfo truck, VehicleLocationPoint location) => new()
    { TruckId = truck.TruckId, TruckExternalId = truck.TruckExternalId, UnitNumber = truck.UnitNumber,
      DriverName = truck.DriverName, TrailerNumber = truck.TrailerNumber, Latitude = location.Latitude,
      Longitude = location.Longitude, Speed = location.Speed, Heading = location.Heading,
      UpdatedAt = location.UpdatedAt, ObservedAt = observedAt, FormattedLocation = location.FormattedLocation };
    var trucks = vehicles.Where(x => x.UpdatedAt != default && active.ContainsKey(x.ExternalId)).Select(x =>
    {
      var item = Map(active[x.ExternalId], new() { Latitude = x.Latitude, Longitude = x.Longitude, Speed = x.Speed,
        Heading = x.Heading, UpdatedAt = x.UpdatedAt, FormattedLocation = x.FormattedLocation });
      item.ObservedAt = x.ObservedAt; item.EngineState = x.EngineState; item.FuelPercent = x.FuelPercent; item.FuelUpdatedAt = x.FuelUpdatedAt;
      return item;
    }).ToList();
    IReadOnlyList<TruckLocationPoint> stream = [];
    if (highFrequency)
    {
      try { stream = await services.GetRequiredService<FleetLocationStream>().GetAsync(services.GetRequiredService<IFleetTelemetryProvider>(), fleet, ct); }
      catch (HttpRequestException) { logger.LogWarning("High-frequency locations unavailable; using the telemetry feed."); }
    }
    var points = (telemetry.Current?.Points ?? []).Concat(stream)
      .Concat(updates.Where(x => active.ContainsKey(x.ExternalId)).Select(x => new TruckLocationPoint(
        active[x.ExternalId].TruckExternalId, x.Latitude, x.Longitude, x.Speed, x.Heading, x.UpdatedAt)))
      .Where(x => x.UpdatedAt > DateTime.UtcNow.AddMinutes(-2)).DistinctBy(x => (x.TruckExternalId, x.UpdatedAt)).OrderBy(x => x.UpdatedAt).ToList();
    telemetry.Set(new() { Trucks = trucks, Points = points });
  }

  private async Task PlanningLoopAsync(CancellationToken ct)
  {
    while (true)
    {
      await RunJobAsync("planning", config.PlanningSeconds, async (services, token) =>
      {
        var mediator = services.GetRequiredService<ISender>();
        var rows = new List<Application.Features.Dispatch.Models.TruckDispatchBoardResponse>();
        for (var page = 1; ; page++)
        {
          var board = await mediator.Send(new GetDispatchBoardQuery(Page: page, PageSize: 100, IncludeHos: false, IncludeFinancials: false, IncludeEta: false), token);
          if (!board.Success || board.Response is null) throw new InvalidOperationException("Dispatch board is unavailable.");
          rows.AddRange(board.Response.Items.Where(x => x.TruckId.HasValue && x.Dispatches.Count > 0));
          if (!board.Response.HasNextPage) break;
        }
        List<Guid> selected;
        lock (stateGate)
        {
          var ids = rows.Select(x => x.TruckId!.Value).ToHashSet();
          state.PendingTrucks.RemoveAll(x => !ids.Contains(x));
          if (state.PendingTrucks.Count == 0 || state.LastPlanningScan == default)
            foreach (var id in ids) if (!state.PendingTrucks.Contains(id)) state.PendingTrucks.Add(id);
          state.LastPlanningScan = DateTime.UtcNow;
          selected = state.PendingTrucks.Take(config.MaxTrucksPerPlanningCycle).ToList();
        }
        foreach (var id in selected)
        {
          var row = rows.First(x => x.TruckId == id);
          await RunJobAsync($"truck:{id}", config.PlanningSeconds, async (truckServices, truckToken) =>
          {
            var planner = truckServices.GetRequiredService<ISender>();
            var prepared = await planner.Send(new PrepareTruckPlanningCommand(id), truckToken);
            if (!prepared.Success || prepared.Response is null)
              throw new InvalidOperationException("Route preparation will retry.");
            var current = prepared.Response;
            if (current.State?.Plan is null && current.DispatchId.HasValue)
              throw new InvalidOperationException("Route preparation will retry.");
            var index = row.Dispatches.FindIndex(x => x.Id == current.DispatchId);
            if (index >= 0)
              foreach (var next in row.Dispatches.Skip(index + 1).Take(config.UpcomingRoutesPerTruck))
              {
                var upcoming = await planner.Send(new PrepareUpcomingPlanningCommand(next.Id), truckToken);
                if (!upcoming.Success) throw new InvalidOperationException("Upcoming route preparation will retry.");
              }
          }, token);
          lock (stateGate) state.PendingTrucks.Remove(id);
        }
      }, ct);
      await Task.Delay(TimeSpan.FromSeconds(1), ct);
    }
  }

  private async Task RunJobAsync(string name, int interval, Func<IServiceProvider, CancellationToken, Task> run, CancellationToken ct)
  {
    lock (stateGate)
    {
      if (!state.Jobs.TryGetValue(name, out var job)) state.Jobs[name] = job = new();
      if (job.NextRun > DateTime.UtcNow) return;
    }
    using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
    timeout.CancelAfter(TimeSpan.FromSeconds(config.JobTimeoutSeconds));
    try
    {
      using var scope = scopes.CreateScope();
      await run(scope.ServiceProvider, timeout.Token);
      lock (stateGate) state.Jobs[name].Success(DateTime.UtcNow, interval);
    }
    catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
    catch (Exception ex)
    {
      lock (stateGate) state.Jobs[name].Fail(DateTime.UtcNow, config.RetrySeconds, ex.GetType().Name);
      logger.LogWarning(ex, "Synchronization job {Job} failed; retaining saved data and retrying.", name);
    }
  }

  private string StateJson() { lock (stateGate) return JsonSerializer.Serialize(state, Json); }

  private async Task CheckpointLoopAsync(CancellationToken ct)
  {
    while (true)
    {
      await Task.Delay(TimeSpan.FromSeconds(config.CheckpointSeconds), ct);
      using var scope = scopes.CreateScope();
      await scope.ServiceProvider.GetRequiredService<ISynchronizationStore>().SaveAsync(owner, StateJson(), ct);
    }
  }
}
