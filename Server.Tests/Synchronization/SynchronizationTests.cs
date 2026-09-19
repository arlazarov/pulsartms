using Application.Features.Routing.Services.Routes;
using Application.Caching;
using Application.Features.Fleet.Services;
using Application.Features.Synchronization.Models;
using Application.Features.Synchronization.Options;
using Application.Features.Synchronization.Services;
using Application.Features.Routing.Interfaces;
using Application.Features.Routing.Services;
using System.Text.Json;
using Application.Features.Routing.Models;
using Application.Features.Dispatch.Commands.SyncDispatche;
using Application.Features.Dispatch.Interfaces;
using Application.Features.Dispatch.Models;
using Application.Features.Dispatch.Queries;
using Application.Features.Fleet.Commands.SyncFleet;
using Application.Features.Fleet.Interfaces;
using Application.Features.Fleet.Models;
using Application.Features.Fleet.Queries.GetFleetLocations;
using Application.Models;
using Domain.Entities.Dispatch;
using Domain.Entities.Fleet;
using Infrastructure.Persistence;
using Infrastructure.Synchronization;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using System.Data.Common;

namespace Server.Tests.Synchronization;

using Dispatch = global::Domain.Entities.Dispatch.Dispatch;

[Trait("Category", "Synchronization")]
public class SynchronizationTests
{
  [Theory]
  [InlineData("\"\"")]
  [InlineData("null")]
  public void SamsaraOngoingAssignmentsAcceptEmptyEndTime(string endTime)
  {
    var json = "{\"startTime\":\"2026-09-05T12:00:00Z\",\"endTime\":" + endTime + "}";
    var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
    Assert.Null(JsonSerializer.Deserialize<Infrastructure.Integrations.Samsara.Models.SamsaraVehicleAssignment>(json, options)!.EndTime);
    Assert.Null(JsonSerializer.Deserialize<Infrastructure.Integrations.Samsara.Models.SamsaraTrailerAssignment>(json, options)!.EndTime);
  }

  [Fact]
  public async Task WorkerRunsWithoutBrowserAndRestartsFromCheckpointWithoutRepeatingCatalog()
  {
    await using var fixture = await Database.CreateAsync();
    fixture.Db.Trucks.Add(new() { Id = Guid.NewGuid(), ExternalId = "truck", UnitNumber = "1", IsActive = true });
    await fixture.Db.SaveChangesAsync();
    var sender = new Sender();
    var feed = new FeedProvider();
    var config = Options.Create(new SynchronizationOptions { Enabled = true, HighFrequencyLocations = false,
      TelemetrySeconds = 1, CheckpointSeconds = 1, PlanningSeconds = 1, RetrySeconds = 1 });
    var services = new ServiceCollection().AddMemoryCache();
    services.AddSingleton<IOptions<SynchronizationOptions>>(config);
    services.AddSingleton<ReadCache>(); services.AddSingleton<ServerTelemetry>();
    services.AddScoped<FleetCache>();
    services.AddDbContext<AppDbContext>(options => options.UseSqlite($"Data Source={fixture.Path}"));
    services.AddScoped<Application.Interfaces.IAppDbContext>(p => p.GetRequiredService<AppDbContext>());
    services.AddScoped<Application.Features.Synchronization.Interfaces.ISynchronizationStore, SynchronizationStore>();
    services.AddSingleton<ISender>(sender); services.AddSingleton<IFleetTelemetryFeedProvider>(feed);
    services.AddSingleton<Application.Features.Dispatch.Interfaces.IDispatchBoardReader>(sender);
    await using var provider = services.BuildServiceProvider();
    var telemetry = provider.GetRequiredService<ServerTelemetry>();
    ApplicationWorker<Application.Features.Synchronization.Interfaces.IFleetSynchronizationOperation> Worker() => new(new FleetSynchronizationOperation(provider.GetRequiredService<IServiceScopeFactory>(), config, telemetry, NullLogger<FleetSynchronizationOperation>.Instance, Options.Create(new Application.Options.HostingOptions())));
    using (var worker = Worker())
    {
      await worker.StartAsync(default);
      await EventuallyAsync(() => feed.Cursors.Count >= 2 && telemetry.Current?.Trucks.Count == 1);
      await worker.StopAsync(default);
    }
    Assert.Equal(1, sender.CatalogCalls);
    Assert.Equal(1, sender.AssignmentCalls);
    Assert.Equal(1, sender.DispatchCalls);
    var saved = await new SynchronizationStore(fixture.Db).ReadAsync(default);
    Assert.NotNull(saved.Jobs["telemetry"].LastSuccess);
    Assert.NotNull(saved.TelemetryCursor);
    Assert.Single(saved.Vehicles);
    var previousCount = feed.Cursors.Count;
    using (var worker = Worker())
    {
      await worker.StartAsync(default);
      await EventuallyAsync(() => feed.Cursors.Count > previousCount);
      await worker.StopAsync(default);
    }
    Assert.Equal(saved.TelemetryCursor, feed.Cursors.ToArray()[previousCount]);
    Assert.Equal(1, sender.CatalogCalls);
    Assert.Equal(1, sender.DispatchCalls);
  }

  [Fact]
  public async Task ApiRoleFollowsTheOwnerCheckpointWithoutTakingTheLease()
  {
    await using var fixture = await Database.CreateAsync();
    fixture.Db.Trucks.Add(new() { Id = Guid.NewGuid(), ExternalId = "truck", UnitNumber = "1", IsActive = true });
    await fixture.Db.SaveChangesAsync();
    var store = new SynchronizationStore(fixture.Db);
    Assert.True(await store.AcquireAsync("owner", DateTime.UtcNow, default));
    var json = new JsonSerializerOptions(JsonSerializerDefaults.Web);
    var checkpoint = new SynchronizationState();
    var at = DateTime.UtcNow.AddMinutes(-1);
    checkpoint.Apply([new("truck", new() { Latitude = 40, Longitude = -80, UpdatedAt = at }, null, null, null, null)], "cursor-1");
    await store.SaveAsync("owner", JsonSerializer.Serialize(checkpoint, json), default);
    var config = Options.Create(new SynchronizationOptions { Enabled = true, HighFrequencyLocations = false, FollowSeconds = 1 });
    var services = new ServiceCollection().AddMemoryCache();
    services.AddSingleton<IOptions<SynchronizationOptions>>(config);
    services.AddSingleton<ReadCache>(); services.AddSingleton<ServerTelemetry>();
    services.AddScoped<FleetCache>();
    services.AddDbContext<AppDbContext>(options => options.UseSqlite($"Data Source={fixture.Path}"));
    services.AddScoped<Application.Interfaces.IAppDbContext>(p => p.GetRequiredService<AppDbContext>());
    services.AddScoped<Application.Features.Synchronization.Interfaces.ISynchronizationStore, SynchronizationStore>();
    await using var provider = services.BuildServiceProvider();
    var telemetry = provider.GetRequiredService<ServerTelemetry>();
    var operation = new FleetSynchronizationOperation(provider.GetRequiredService<IServiceScopeFactory>(), config, telemetry,
      NullLogger<FleetSynchronizationOperation>.Instance, Options.Create(new Application.Options.HostingOptions { Role = Application.Options.HostingRole.Api }));
    using var worker = new ApplicationWorker<Application.Features.Synchronization.Interfaces.IFleetSynchronizationOperation>(operation);
    await worker.StartAsync(default);
    await EventuallyAsync(() => telemetry.Current?.Trucks.Count == 1);
    var revision = telemetry.Current!.Revision;
    Assert.Equal(40, telemetry.Current.Trucks[0].Latitude);
    await Task.Delay(TimeSpan.FromSeconds(2.5));
    // An unchanged checkpoint keeps the revision so browser validators keep matching.
    Assert.Equal(revision, telemetry.Current!.Revision);
    Assert.False(operation.Status.Active);
    Assert.True(await store.RenewAsync("owner", DateTime.UtcNow, default));
    checkpoint.Apply([new("truck", new() { Latitude = 41, Longitude = -80, UpdatedAt = at.AddSeconds(30) }, null, null, null, null)], "cursor-2");
    await store.SaveAsync("owner", JsonSerializer.Serialize(checkpoint, json), default);
    await EventuallyAsync(() => telemetry.Current!.Revision != revision);
    Assert.Equal(41, telemetry.Current!.Trucks[0].Latitude);
    await worker.StopAsync(default);
  }

  [Fact]
  public async Task DisabledWorkerDoesNotResolveDatabaseOrProviders()
  {
    await using var services = new ServiceCollection().BuildServiceProvider();
    using var worker = new ApplicationWorker<Application.Features.Synchronization.Interfaces.IFleetSynchronizationOperation>(new FleetSynchronizationOperation(services.GetRequiredService<IServiceScopeFactory>(),
      Options.Create(new SynchronizationOptions { Enabled = false }), new ServerTelemetry(), NullLogger<FleetSynchronizationOperation>.Instance, Options.Create(new Application.Options.HostingOptions())));
    await worker.StartAsync(default);
    await worker.ExecuteTask!;
    await worker.StopAsync(default);
  }

  private static async Task EventuallyAsync(Func<bool> condition)
  {
    using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(8));
    while (!condition()) await Task.Delay(25, timeout.Token);
  }

  [Fact]
  public void TelemetryKeepsIndependentTimestampsAndResumesFromSerializedCursor()
  {
    var now = DateTime.UtcNow;
    var state = new SynchronizationState();
    state.Apply([new("truck", new() { UpdatedAt = now, Latitude = 40, Longitude = -80 }, "On", now, 50, now)], "page-1");
    state.Apply([new("truck", new() { UpdatedAt = now.AddSeconds(-10), Latitude = 1 }, "Off", now.AddSeconds(-10), 30, now.AddSeconds(1))], "page-2");
    var saved = JsonSerializer.Deserialize<SynchronizationState>(JsonSerializer.Serialize(state))!;
    Assert.Equal("page-2", saved.TelemetryCursor);
    Assert.Equal(40, saved.Vehicles["truck"].Latitude);
    Assert.Equal("On", saved.Vehicles["truck"].EngineState);
    Assert.Equal(30, saved.Vehicles["truck"].FuelPercent);
  }

  [Fact]
  public async Task LeasePreventsSecondWorkerAndRejectsOldOwnerAfterTakeover()
  {
    await using var fixture = await Database.CreateAsync();
    var store = new SynchronizationStore(fixture.Db);
    var now = DateTime.UtcNow;
    Assert.True(await store.AcquireAsync("first", now, default));
    Assert.False(await store.AcquireAsync("second", now.AddSeconds(1), default));
    await store.SaveAsync("first", "{\"telemetryCursor\":\"saved\"}", default);
    Assert.Equal("saved", (await store.ReadAsync(default)).TelemetryCursor);
    Assert.True(await store.AcquireAsync("second", now.AddMinutes(4), default));
    Assert.False(await store.RenewAsync("first", now.AddMinutes(4), default));
    await Assert.ThrowsAsync<InvalidOperationException>(() => store.SaveAsync("first", "{}", default));
    await store.ReleaseAsync("first", default);
    Assert.Equal("second", await fixture.Db.SynchronizationCheckpoints.Select(x => x.Owner).SingleAsync());
  }

  [Fact]
  public async Task UnchangedAssignmentsDoNotWriteAndSwapsPreserveUniqueConstraints()
  {
    await using var fixture = await Database.CreateAsync();
    var db = fixture.Db;
    var a = new Driver { Id = Guid.NewGuid(), ExternalId = "a", Name = "A", IsActive = true };
    var b = new Driver { Id = Guid.NewGuid(), ExternalId = "b", Name = "B", IsActive = true };
    var trailerA = new Trailer { Id = Guid.NewGuid(), ExternalId = "ta", UnitNumber = "TA", IsActive = true };
    var trailerB = new Trailer { Id = Guid.NewGuid(), ExternalId = "tb", UnitNumber = "TB", IsActive = true };
    var one = new Truck { Id = Guid.NewGuid(), ExternalId = "one", UnitNumber = "1", IsActive = true, Driver = a, Trailer = trailerA };
    var two = new Truck { Id = Guid.NewGuid(), ExternalId = "two", UnitNumber = "2", IsActive = true, Driver = b, Trailer = trailerB };
    db.AddRange(one, two); await db.SaveChangesAsync();
    var now = DateTime.UtcNow;
    var assignments = new[] { new ExternalFleetAssignment { DriverExternalId = "a", VehicleExternalId = "one", StartTime = now.AddDays(-1) },
      new ExternalFleetAssignment { DriverExternalId = "b", VehicleExternalId = "two", StartTime = now.AddDays(-1) } };
    var trailers = new[] { new ExternalTrailerAssignment { DriverExternalId = "a", TrailerExternalId = "ta", StartTime = now.AddDays(-1) },
      new ExternalTrailerAssignment { DriverExternalId = "b", TrailerExternalId = "tb", StartTime = now.AddDays(-1) } };
    Assert.Equal(0, await FleetAssignmentSync.SyncAsync(db, assignments, trailers, now));
    Assert.Equal(0, await db.SaveChangesAsync());
    assignments[0].VehicleExternalId = "two"; assignments[1].VehicleExternalId = "one";
    await using var transaction = await db.Database.BeginTransactionAsync();
    await FleetAssignmentSync.SyncAsync(db, assignments, trailers, now);
    await db.SaveChangesAsync(); await transaction.CommitAsync();
    Assert.Equal(b.Id, one.DriverId); Assert.Equal(trailerB.Id, one.TrailerId);
    Assert.Equal(a.Id, two.DriverId); Assert.Equal(trailerA.Id, two.TrailerId);
  }

  [Fact]
  public async Task RepeatedDispatchSyncDoesNotWriteAndStatusUpdatePreservesStopIds()
  {
    await using var fixture = await Database.CreateAsync();
    var source = new ExternalDispatch { LoadNumber = 1, Status = "assigned", Stops = [new() { Sequence = 1, Job = "Pick Up", City = "Buffalo" }] };
    using var reads = TestCache.Create();
    using var memory = new MemoryCache(new MemoryCacheOptions());
    using var gates = new Application.Caching.ProcessGates();
    var handler = new SyncDispatchesCommandHandler(fixture.Db, new DispatchProvider(source), reads, memory,
      new(Microsoft.Extensions.Options.Options.Create(new Application.Features.Routing.Options.RoutePreparationOptions()), TimeProvider.System), new(gates));
    await handler.Handle(new(), default);
    var load = await fixture.Db.Dispatches.Include(x => x.Stops).SingleAsync();
    var id = load.Stops.Single().Id;
    var synced = load.LastSyncedAt;
    Assert.Equal(0, (await handler.Handle(new(), default)).Response);
    Assert.Equal(synced, load.LastSyncedAt);
    source.Status = "in_transit";
    source.Stops.Single().PickedUpAt = DateTime.UtcNow;
    Assert.True((await handler.Handle(new(), default)).Response > 0);
    Assert.Equal(id, load.Stops.Single().Id);
    Assert.NotNull(load.Stops.Single().PickedUpAt);
  }

  [Fact]
  public void RecentlyPreparedRouteSkipsQueueButMissingOrChangedInputsDoNot()
  {
    using var memory = new MemoryCache(new MemoryCacheOptions());
    var queue = new PlanningRefreshQueue(memory, Options.Create(new SynchronizationOptions()));
    var plan = new RoutePlan { CalculatedAt = DateTime.UtcNow,
      FuelRecommendations = new() { Version = 2, CalculatedAt = DateTime.UtcNow } };
    var state = new RoutePlanningState(new(), plan, null, null, null, true);
    Assert.False(queue.Enqueue(Guid.NewGuid(), state));
    plan.InputsChanged = true;
    Assert.True(queue.Enqueue(Guid.NewGuid(), state));
    plan.InputsChanged = false;
    plan.FuelRecommendations = null;
    Assert.False(queue.Enqueue(Guid.NewGuid(), state));
    plan.FuelRecommendations = new() { Version = 2, CalculatedAt = DateTime.UtcNow };
    plan.CalculatedAt = DateTime.UtcNow.AddMinutes(-3);
    var id = Guid.NewGuid();
    Assert.True(queue.Enqueue(id, state));
    queue.Complete(id, true);
    Assert.False(queue.Enqueue(id, state));
    Assert.True(queue.Enqueue(Guid.NewGuid()));
  }

  [Fact]
  public void RouteReadStatusDoesNotInventAnActiveCalculationAndPreservesProviderErrors()
  {
    using var memory = new MemoryCache(new MemoryCacheOptions());
    var queue = new PlanningRefreshQueue(memory, Options.Create(new SynchronizationOptions()));
    var id = Guid.NewGuid();
    var state = new RoutePlanningState(new(), null, null, null, null, true);
    Assert.Equal("Route update pending.", queue.Message(id, state));
    queue.Enqueue(id, state);
    Assert.Equal("Route update queued.", queue.Message(id, state));
    memory.Set($"automatic-planning-error:{id}:{PlanningSettingsService.Signature(state.Profile)}", "Address lookup failed.");
    Assert.Equal("Address lookup failed.", queue.Message(id, state));
  }

  [Fact]
  public async Task CachedTelemetryReadsNeverWaitForProviderOrExpiredCache()
  {
    using var memory = new MemoryCache(new MemoryCacheOptions());
    using var telemetry = new FleetTelemetryCache(memory);
    using var stream = new FleetLocationStream(TimeProvider.System);
    var handler = new GetFleetLocationsHandler(null!, null!, null!, telemetry, stream, new(), Options.Create(new SynchronizationOptions()));
    Assert.Empty((await handler.Handle(new(CachedOnly: true), default)).Response!.Trucks);
    var snapshot = new FleetLocationsResponse { Trucks = [new() { TruckId = Guid.NewGuid() }] };
    await telemetry.GetAsync(_ => Task.FromResult(snapshot), default);
    memory.Remove(FleetTelemetryCache.CacheKey);
    Assert.Same(snapshot, (await handler.Handle(new(CachedOnly: true), default)).Response);
  }

  [Theory]
  [InlineData(true, 2)]
  [InlineData(false, 2)]
  [InlineData(true, 30001)]
  public async Task WarmRouteReadsAndProgressDoNotQueryDatabaseOrRoutingProvider(bool enabled, int pointCount)
  {
    await using var fixture = await Database.CreateAsync();
    var db = fixture.Db;
    var truck = new Truck { Id = Guid.NewGuid(), ExternalId = "truck", UnitNumber = "1", IsActive = true };
    var load = new Domain.Entities.Dispatch.Dispatch { Id = Guid.NewGuid(), Truck = truck, Status = "assigned", LoadNumber = 1,
      Stops = [new() { Id = Guid.NewGuid(), Sequence = 1, Job = "Pick Up", Latitude = 40, Longitude = -80, PickedUpAt = DateTime.UtcNow.AddHours(-1) },
        new() { Id = Guid.NewGuid(), Sequence = 2, Job = "Drop Off", Latitude = 40, Longitude = -79 }] };
    db.Dispatches.Add(load); await db.SaveChangesAsync();
    load = await db.Dispatches.AsNoTracking().Include(x => x.Stops).SingleAsync();
    var profile = new TruckRouteProfile();
    var route = new TruckRoute { Miles = 100, Seconds = 6000, Points = [new(40, -80), new(40, -79)], Legs = [new(100, 6000, [new(40, -80), new(40, -79)])] };
    if (pointCount > 2)
    {
      route.Points = Enumerable.Range(0, pointCount).Select(i => new RoutePoint(40, -80 + i / (double)(pointCount - 1))).ToList();
      route.Legs = [new(100, 6000, route.Points)];
    }
    var plan = new RoutePlan { Id = Guid.NewGuid(), DispatchId = load.Id, TruckId = truck.Id, Route = route, Profile = profile,
      Stops = load.Stops.Select(x => new PlanStop(x.Id, "", "", x.Sequence, new((double)x.Latitude!, (double)x.Longitude!))).ToList() };
    db.DispatchRoutePlans.Add(new() { Id = plan.Id, DispatchId = load.Id, TruckId = truck.Id, InputHash = RoutePlanningService.HashInputs(load, profile), PlanJson = JsonSerializer.Serialize(plan, RoutePlanningService.Json) });
    await db.SaveChangesAsync(); db.ChangeTracker.Clear();
    using var memory = new MemoryCache(new MemoryCacheOptions());
    using var cache = new ReadCache(Options.Create(new SynchronizationOptions()));
    var sender = new Sender();
    sender.Fleet.Trucks = [new() { TruckId = truck.Id, Latitude = 40, Longitude = -79.5m, UpdatedAt = DateTime.UtcNow }];
    using var displays = new RouteDisplayCache(cache);
    using var services = new PlanningTestServices(db, new NoRouter(), sender, cache);
    var routes = services.Routes;
    await routes.GetAsync(load.Id, default);
    await routes.GetAsync(load, default, displayOnly: true);
    var options = Options.Create(new SynchronizationOptions { Enabled = enabled });
    var queue = new PlanningRefreshQueue(memory, options);
    // The warm-read budget covers route and progress reads; the board itself is stubbed empty here.
    var browser = new PlanningReadService(routes, queue, new(sender, services.Forecasts), options, services.Eta, services.FuelPlans);
    Assert.NotNull((await browser.ForDispatchAsync(load.Id, default)).State?.Plan);
    var reads = fixture.Counter.Reads;
    for (var i = 0; i < 20; i++)
    {
      var state = await routes.GetAsync(load.Id, default);
      Assert.Equal(50, state.Progress!.RemainingMiles!.Value, 3);
    }
    for (var i = 0; i < 20; i++)
      Assert.NotNull((await browser.ForDispatchAsync(load.Id, default)).State?.Plan);
    Assert.Equal(enabled, queue.Enqueue(load.Id));
    queue.Complete(load.Id, true);
    Assert.False(queue.Enqueue(load.Id));
    Assert.Equal(reads, fixture.Counter.Reads);
    await routes.AdvanceAutomaticallyAsync(load.Id, default);
    await routes.GetAsync(load.Id, default);
    reads = fixture.Counter.Reads;
    await routes.AdvanceAutomaticallyAsync(load.Id, default);
    await routes.GetAsync(load.Id, default);
    Assert.Equal(reads, fixture.Counter.Reads);
  }

  [Fact]
  public async Task SlowCacheMissDoesNotBlockUnrelatedData()
  {
    using var memory = new MemoryCache(new MemoryCacheOptions());
    using var cache = new ReadCache(Options.Create(new SynchronizationOptions()));
    var started = new TaskCompletionSource();
    var release = new TaskCompletionSource();
    var first = cache.GetAsync("slow", "key", async () => { started.SetResult(); await release.Task; return 1; });
    await started.Task;
    var key = Enumerable.Range(0, 1000).Select(x => x.ToString()).First(x =>
      (uint)StringComparer.Ordinal.GetHashCode($"read:fast:0:{x}") % 64 !=
      (uint)StringComparer.Ordinal.GetHashCode("read:slow:0:key") % 64);
    try { Assert.Equal(2, await cache.GetAsync("fast", key, () => Task.FromResult(2)).WaitAsync(TimeSpan.FromSeconds(2))); }
    finally { release.SetResult(); await first; }
  }

  [Fact]
  public async Task ReadCacheSharesConcurrentLoadsAndDoesNotLeakMutations()
  {
    using var memory = new MemoryCache(new MemoryCacheOptions());
    using var cache = new ReadCache(Options.Create(new SynchronizationOptions()));
    var calls = 0;
    async Task<List<string>> Load() { Interlocked.Increment(ref calls); await Task.Yield(); return ["saved"]; }
    var results = await Task.WhenAll(Enumerable.Range(0, 10).Select(_ => cache.GetAsync("test", "key", Load)));
    Assert.Equal(1, calls);
    results[0].Clear();
    Assert.Equal("saved", Assert.Single(await cache.GetAsync("test", "key", Load)));
    cache.Invalidate("test");
    await cache.GetAsync("test", "key", Load);
    Assert.Equal(2, calls);
  }

  [Fact]
  public void FailureBackoffRetainsLastSuccessAndRecovers()
  {
    var now = DateTime.UtcNow;
    var job = new SyncJobState();
    job.Success(now, 120);
    job.Fail(now.AddMinutes(2), 60, "HTTP");
    Assert.Equal(now, job.LastSuccess);
    Assert.Equal(now.AddMinutes(3), job.NextRun);
    job.Fail(now.AddMinutes(3), 60, "HTTP");
    Assert.Equal(now.AddMinutes(5), job.NextRun);
    job.Success(now.AddMinutes(5), 120);
    Assert.Null(job.Error); Assert.Equal(0, job.Failures);
  }

  private sealed class DispatchProvider(ExternalDispatch source) : IDispatchProvider
  {
    public Task<IReadOnlyList<ExternalDispatch>> GetDispatchesAsync(CancellationToken ct = default) => Task.FromResult<IReadOnlyList<ExternalDispatch>>([source]);
    public Task<IReadOnlyList<ExternalDispatch>> GetDispatchesAsync(DateOnly from, DateOnly to, CancellationToken ct = default) => GetDispatchesAsync(ct);
  }

  private sealed class FeedProvider : IFleetTelemetryFeedProvider
  {
    public System.Collections.Concurrent.ConcurrentQueue<string?> Cursors = new();
    public Task<TelemetryFeed> GetFeedAsync(string? cursor, CancellationToken ct)
    {
      Cursors.Enqueue(cursor);
      var now = DateTime.UtcNow;
      return Task.FromResult(new TelemetryFeed([new("truck", new() { ExternalId = "truck", Latitude = 40,
        Longitude = -80, UpdatedAt = now }, "On", now, 50, now)], $"cursor-{Cursors.Count}", false));
    }
  }

  internal sealed class Sender : ISender, Application.Features.Dispatch.Interfaces.IDispatchBoardReader
  {
    public FleetLocationsResponse Fleet { get; } = new();
    public int CatalogCalls, AssignmentCalls, DispatchCalls;
    public Task<PaginatedList<TruckDispatchBoardResponse>> ReadAsync(GetDispatchBoardQuery request, CancellationToken ct) =>
      Task.FromResult(new PaginatedList<TruckDispatchBoardResponse> { Items = [], Page = 1, PageSize = 100, TotalCount = 0 });
    public Task<TResponse> Send<TResponse>(IRequest<TResponse> request, CancellationToken ct = default)
    {
      object result = request switch
      {
        GetFleetLocationsQuery => RequestResponse<FleetLocationsResponse>.Ok(Fleet),
        SyncFleetCommand => RequestResponse<int>.Ok(Interlocked.Increment(ref CatalogCalls)),
        SyncAssignmentsCommand => RequestResponse<int>.Ok(Interlocked.Increment(ref AssignmentCalls)),
        SyncDispatchesCommand => RequestResponse<int>.Ok(Interlocked.Increment(ref DispatchCalls)),
        _ => throw new NotSupportedException()
      };
      return Task.FromResult((TResponse)result);
    }
    public Task Send<TRequest>(TRequest request, CancellationToken ct = default) where TRequest : IRequest => throw new NotSupportedException();
    public Task<object?> Send(object request, CancellationToken ct = default) => throw new NotSupportedException();
    public IAsyncEnumerable<TResponse> CreateStream<TResponse>(IStreamRequest<TResponse> request, CancellationToken ct = default) => throw new NotSupportedException();
    public IAsyncEnumerable<object?> CreateStream(object request, CancellationToken ct = default) => throw new NotSupportedException();
  }

  private sealed class NoRouter : IRoutingProvider
  {
    public bool IsConfigured => true;
    public Task<TruckRoute> CalculateAsync(IReadOnlyList<RoutePoint> points, TruckRouteProfile profile, CancellationToken ct) => throw new Exception("Unexpected routing request");
    public Task<RoutePoint> GeocodeAsync(string address, CancellationToken ct) => throw new Exception("Unexpected geocoding request");
  }

  private sealed class Counter : DbCommandInterceptor
  {
    public int Reads;
    public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken ct = default)
    { Interlocked.Increment(ref Reads); return ValueTask.FromResult(result); }
  }

  private sealed class Database : IAsyncDisposable
  {
    public required AppDbContext Db;
    public required string Path;
    public Counter Counter = new();
    public static async Task<Database> CreateAsync()
    {
      var path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"amftms-sync-{Guid.NewGuid():N}.db");
      var counter = new Counter();
      var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseSqlite($"Data Source={path}").AddInterceptors(counter).Options);
      await db.Database.EnsureCreatedAsync();
      return new() { Db = db, Path = path, Counter = counter };
    }
    public async ValueTask DisposeAsync() { await Db.DisposeAsync(); File.Delete(Path); }
  }
}
