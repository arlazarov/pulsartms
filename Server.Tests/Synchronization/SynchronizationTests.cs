using System.Collections.Concurrent;
using System.Data.Common;
using System.Text.Json;
using Application.Caching;
using Application.Features.Dispatch.Commands.SyncDispatche;
using Application.Features.Dispatch.Interfaces;
using Application.Features.Dispatch.Models;
using Application.Features.Dispatch.Options;
using Application.Features.Dispatch.Queries;
using Application.Features.Fleet.Commands.SyncFleet;
using Application.Features.Fleet.Interfaces;
using Application.Features.Fleet.Queries.GetFleetLocations;
using Application.Features.Fleet.Services;
using Application.Features.Fuel.Interfaces;
using Application.Features.Fuel.Models;
using Application.Features.Fuel.Services;
using Application.Features.Routing.Background;
using Application.Features.Routing.Interfaces;
using Application.Features.Routing.Services;
using Application.Features.Routing.Services.Routes;
using Application.Features.Synchronization.Interfaces;
using Application.Features.Synchronization.Models;
using Application.Features.Synchronization.Options;
using Application.Features.Synchronization.Services;
using Application.Interfaces;
using Application.Models;
using Domain.Entities;
using Domain.Entities.Dispatch;
using Domain.Entities.Fleet;
using Domain.Models.Fleet;
using Domain.Models.Routing;
using Domain.Policies;
using Domain.Rules;
using Domain.Rules.Routing;
using Infrastructure.Integrations.Samsara.Models;
using Infrastructure.Persistence;
using Infrastructure.Synchronization;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Server.Tests.Support;

namespace Server.Tests.Synchronization;

using Dispatch = global::Domain.Entities.Dispatch.Dispatch;
using FilePath = System.IO.Path;

[Trait("Category", "Synchronization")]
public class SynchronizationTests
{
  [Theory]
  [InlineData("\"\"")]
  [InlineData("null")]
  public void SamsaraOngoingAssignmentsAcceptEmptyEndTime(string endTime)
  {
    var json =
      "{\"startTime\":\"2026-09-05T12:00:00Z\",\"endTime\":" + endTime + "}";
    var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
    Assert.Null(
      JsonSerializer
        .Deserialize<SamsaraVehicleAssignment>(json, options)!
        .EndTime
    );
    Assert.Null(
      JsonSerializer
        .Deserialize<SamsaraTrailerAssignment>(json, options)!
        .EndTime
    );
  }

  [Theory]
  [InlineData(true)]
  [InlineData(false)]
  public async Task WorkerRunsWithoutBrowserAndRestartsFromCheckpointWithoutRepeatingCatalog(
    bool importEnabled
  )
  {
    await using var fixture = await Database.CreateAsync();
    fixture.Db.Trucks.Add(
      new()
      {
        Id = Guid.NewGuid(),
        ExternalId = "truck",
        UnitNumber = "1",
        IsActive = true,
      }
    );
    await fixture.Db.SaveChangesAsync();
    var sender = new Sender();
    var feed = new FeedProvider();
    var config = Options.Create(
      new SynchronizationOptions
      {
        Enabled = true,
        HighFrequencyLocations = false,
        TelemetrySeconds = 1,
        CheckpointSeconds = 1,
        PlanningSeconds = 1,
        RetrySeconds = 1,
      }
    );
    var services = new ServiceCollection().AddMemoryCache();
    services.AddSingleton<IOptions<SynchronizationOptions>>(config);
    services.AddSingleton<ReadCache>();
    services.AddSingleton<ICurrentCompany>(new TestCompany());
    services.AddSingleton<ServerTelemetry>();
    services.AddScoped<FleetCache>();
    services.AddDbContext<AppDbContext>(options =>
      options.UseSqlite($"Data Source={fixture.Path}")
    );
    services.AddScoped<IAppDbContext>(p =>
      p.GetRequiredService<AppDbContext>()
    );
    services.AddScoped<ISynchronizationStore, SynchronizationStore>();
    services.AddSingleton<ISender>(sender);
    services.AddSingleton<IFleetTelemetryFeedProvider>(feed);
    services.AddSingleton<TimeProvider>(TimeProvider.System);
    services.AddSingleton<IFuelExchangeRateStore>(
      new MemoryFuelExchangeRateStore()
    );
    services.AddSingleton<IFuelExchangeRateProvider>(
      new StubFuelExchangeRateProvider
      {
        Read = _ =>
          Task.FromResult(
            new FuelExchangeRate(
              .73m,
              DateOnly.FromDateTime(DateTime.UtcNow),
              DateTime.UtcNow
            )
          ),
      }
    );
    services.AddScoped<FuelExchangeRateService>();
    await using var provider = services.BuildServiceProvider();
    var telemetry = provider.GetRequiredService<ServerTelemetry>();
    ApplicationWorker<IFleetSynchronizationOperation> Worker() =>
      new(
        new FleetSynchronizationOperation(
          provider.GetRequiredService<IServiceScopeFactory>(),
          config,
          Options.Create(
            new DispatchImportOptions
            {
              Provider = importEnabled ? DispatchImportTestData.Key : "",
            }
          ),
          telemetry,
          NullLogger<FleetSynchronizationOperation>.Instance,
          new TestCompany()
        ),
        new ConfigurationBuilder().Build()
      );
    using (var worker = Worker())
    {
      await worker.StartAsync(default);
      await EventuallyAsync(
        () => feed.Cursors.Count >= 2 && telemetry.Current?.Trucks.Count == 1
      );
      await worker.StopAsync(default);
    }
    Assert.Equal(1, sender.CatalogCalls);
    Assert.Equal(1, sender.AssignmentCalls);
    Assert.Equal(importEnabled ? 1 : 0, sender.DispatchCalls);
    var saved = await new SynchronizationStore(fixture.Db).ReadAsync(default);
    Assert.NotNull(saved.Jobs["telemetry"].LastSuccess);
    Assert.NotNull(saved.Jobs["fuel-exchange-rate"].LastSuccess);
    Assert.Equal(0, saved.Jobs["fuel-exchange-rate"].Failures);
    Assert.NotNull(saved.TelemetryCursor);
    Assert.Single(saved.Vehicles);
    Assert.Equal(
      22.5m,
      Assert.Single(telemetry.Current!.Trucks).OutsideTemperatureCelsius
    );
    Assert.Equal(
      saved.Vehicles["truck"].OutsideTemperatureUpdatedAt,
      telemetry.Current.Trucks[0].OutsideTemperatureUpdatedAt
    );
    var previousCount = feed.Cursors.Count;
    using (var worker = Worker())
    {
      await worker.StartAsync(default);
      await EventuallyAsync(() => feed.Cursors.Count > previousCount);
      await worker.StopAsync(default);
    }
    Assert.Equal(saved.TelemetryCursor, feed.Cursors.ToArray()[previousCount]);
    Assert.Equal(1, sender.CatalogCalls);
    Assert.Equal(importEnabled ? 1 : 0, sender.DispatchCalls);
  }

  [Fact]
  public async Task DisabledWorkerDoesNotResolveDatabaseOrProviders()
  {
    await using var services = new ServiceCollection().BuildServiceProvider();
    using var worker = new ApplicationWorker<IFleetSynchronizationOperation>(
      new FleetSynchronizationOperation(
        services.GetRequiredService<IServiceScopeFactory>(),
        Options.Create(new SynchronizationOptions { Enabled = false }),
        DispatchImportTestData.Options,
        new ServerTelemetry(new TestCompany()),
        NullLogger<FleetSynchronizationOperation>.Instance,
        new TestCompany()
      ),
      new ConfigurationBuilder().Build()
    );
    await worker.StartAsync(default);
    await worker.ExecuteTask!;
    await worker.StopAsync(default);
  }

  private static async Task EventuallyAsync(Func<bool> condition)
  {
    using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(8));
    while (!condition())
      await Task.Delay(25, timeout.Token);
  }

  [Fact]
  public void TelemetryKeepsIndependentTimestampsAndResumesFromSerializedCursor()
  {
    var now = DateTime.UtcNow;
    var state = new SynchronizationState();
    state.Apply(
      [
        new(
          "truck",
          new()
          {
            UpdatedAt = now,
            Latitude = 40,
            Longitude = -80,
          },
          "On",
          now,
          50,
          now
        ),
      ],
      "page-1"
    );
    state.Apply(
      [
        new(
          "truck",
          new() { UpdatedAt = now.AddSeconds(-10), Latitude = 1 },
          "Off",
          now.AddSeconds(-10),
          30,
          now.AddSeconds(1)
        ),
      ],
      "page-2"
    );
    var saved = JsonSerializer.Deserialize<SynchronizationState>(
      JsonSerializer.Serialize(state)
    )!;
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
    Assert.False(
      await store.AcquireAsync("second", now.AddSeconds(1), default)
    );
    await store.SaveAsync("first", "{\"telemetryCursor\":\"saved\"}", default);
    Assert.Equal("saved", (await store.ReadAsync(default)).TelemetryCursor);
    Assert.True(await store.AcquireAsync("second", now.AddMinutes(4), default));
    Assert.False(await store.RenewAsync("first", now.AddMinutes(4), default));
    await Assert.ThrowsAsync<InvalidOperationException>(
      () => store.SaveAsync("first", "{}", default)
    );
    await store.ReleaseAsync("first", default);
    Assert.Equal(
      "second",
      await fixture
        .Db.SynchronizationCheckpoints.Select(x => x.Owner)
        .SingleAsync()
    );
  }

  [Fact]
  public async Task UnchangedAssignmentsDoNotWriteAndSwapsPreserveUniqueConstraints()
  {
    await using var fixture = await Database.CreateAsync();
    var db = fixture.Db;
    var a = new Driver
    {
      Id = Guid.NewGuid(),
      ExternalId = "a",
      Name = "A",
      IsActive = true,
    };
    var b = new Driver
    {
      Id = Guid.NewGuid(),
      ExternalId = "b",
      Name = "B",
      IsActive = true,
    };
    var trailerA = new Trailer
    {
      Id = Guid.NewGuid(),
      ExternalId = "ta",
      UnitNumber = "TA",
      IsActive = true,
    };
    var trailerB = new Trailer
    {
      Id = Guid.NewGuid(),
      ExternalId = "tb",
      UnitNumber = "TB",
      IsActive = true,
    };
    var one = new Truck
    {
      Id = Guid.NewGuid(),
      ExternalId = "one",
      UnitNumber = "1",
      IsActive = true,
      Driver = a,
      Trailer = trailerA,
      // As migrated: the trailer already there is telemetry's last word.
      TelemetryTrailerKnown = true,
      TelemetryTrailerId = trailerA.Id,
      TrailerSource = "telemetry",
    };
    var two = new Truck
    {
      Id = Guid.NewGuid(),
      ExternalId = "two",
      UnitNumber = "2",
      IsActive = true,
      Driver = b,
      Trailer = trailerB,
      TelemetryTrailerKnown = true,
      TelemetryTrailerId = trailerB.Id,
      TrailerSource = "telemetry",
    };
    db.AddRange(one, two);
    await db.SaveChangesAsync();
    var now = DateTime.UtcNow;
    var assignments = new[]
    {
      new ExternalFleetAssignment
      {
        DriverExternalId = "a",
        VehicleExternalId = "one",
        StartTime = now.AddDays(-1),
      },
      new ExternalFleetAssignment
      {
        DriverExternalId = "b",
        VehicleExternalId = "two",
        StartTime = now.AddDays(-1),
      },
    };
    var trailers = new[]
    {
      new ExternalTrailerAssignment
      {
        DriverExternalId = "a",
        TrailerExternalId = "ta",
        StartTime = now.AddDays(-1),
      },
      new ExternalTrailerAssignment
      {
        DriverExternalId = "b",
        TrailerExternalId = "tb",
        StartTime = now.AddDays(-1),
      },
    };
    Assert.Equal(
      0,
      await FleetAssignmentSync.SyncAsync(
        db,
        assignments,
        trailers,
        now,
        "telemetry-source"
      )
    );
    Assert.Equal(0, await db.SaveChangesAsync());
    assignments[0].VehicleExternalId = "two";
    assignments[1].VehicleExternalId = "one";
    await using var transaction = await db.Database.BeginTransactionAsync();
    await FleetAssignmentSync.SyncAsync(
      db,
      assignments,
      trailers,
      now,
      "telemetry-source"
    );
    await db.SaveChangesAsync();
    await transaction.CommitAsync();
    Assert.Equal(b.Id, one.DriverId);
    Assert.Equal(trailerB.Id, one.TrailerId);
    Assert.Equal(a.Id, two.DriverId);
    Assert.Equal(trailerA.Id, two.TrailerId);
  }

  [Fact]
  public async Task RepeatedDispatchSyncDoesNotWriteAndStatusUpdatePreservesStopIds()
  {
    await using var fixture = await Database.CreateAsync();
    var source = new ExternalDispatch
    {
      LoadNumber = 1,
      Status = "assigned",
      Stops =
      [
        new()
        {
          Sequence = 1,
          Job = "Pick Up",
          City = "Buffalo",
        },
      ],
    };
    using var reads = TestCache.Create();
    using var memory = new MemoryCache(new MemoryCacheOptions());
    var handler = new SyncDispatchesCommandHandler(
      fixture.Db,
      [new DispatchProvider(source)],
      DispatchImportTestData.Options,
      reads,
      memory,
      new(Options.Create(new RoutePreparationOptions()), TimeProvider.System)
    );
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

  // A trailer only the load import names is catalogued by the import, the
  // load points at it, and its truck takes it once the load is in transit.
  [Fact]
  public async Task AnImportedLoadsUnknownTrailerIsCataloguedAndPutOnItsTruck()
  {
    await using var fixture = await Database.CreateAsync();
    var truck = new Truck
    {
      Id = Guid.NewGuid(),
      ExternalId = "v-11005",
      UnitNumber = "11005",
      IsActive = true,
    };
    fixture.Db.Trucks.Add(truck);
    await fixture.Db.SaveChangesAsync();
    var source = new ExternalDispatch
    {
      LoadNumber = 1407,
      Status = "in_transit",
      TruckNumber = "11005",
      TrailerNumber = "55904",
      Stops =
      [
        new()
        {
          Sequence = 1,
          Job = "Pick Up",
          City = "Buffalo",
          TruckNumber = "11005",
          TrailerNumber = "55904",
        },
        new()
        {
          Sequence = 2,
          Job = "Drop Off",
          City = "Toronto",
          TruckNumber = "11005",
          TrailerNumber = "55904",
        },
      ],
    };
    using var reads = TestCache.Create();
    using var memory = new MemoryCache(new MemoryCacheOptions());
    await new SyncDispatchesCommandHandler(
      fixture.Db,
      [new DispatchProvider(source)],
      DispatchImportTestData.Options,
      reads,
      memory,
      new(Options.Create(new RoutePreparationOptions()), TimeProvider.System)
    ).Handle(new(), default);

    var trailer = await fixture.Db.Trailers.AsNoTracking().SingleAsync();
    Assert.Equal(
      ("55904", DispatchImportTestData.Key),
      (trailer.UnitNumber, trailer.Source)
    );
    var load = await fixture
      .Db.Dispatches.AsNoTracking()
      .Include(x => x.Stops)
      .SingleAsync();
    Assert.Equal(trailer.Id, load.TrailerId);
    Assert.All(load.Stops, x => Assert.Equal(trailer.Id, x.TrailerId));
    var row = await fixture.Db.Trucks.AsNoTracking().SingleAsync();
    Assert.Equal((trailer.Id, "load"), (row.TrailerId, row.TrailerSource));
  }

  [Fact]
  public async Task CachedTelemetryReadsNeverWaitForProviderOrExpiredCache()
  {
    using var memory = new MemoryCache(new MemoryCacheOptions());
    using var telemetry = new FleetTelemetryCache(memory, new TestCompany());
    using var stream = new FleetLocationStream(
      TimeProvider.System,
      new TestCompany()
    );
    var handler = new GetFleetLocationsHandler(
      null!,
      null!,
      null!,
      telemetry,
      stream,
      new(new TestCompany()),
      new NoRecordedPositions(),
      Options.Create(new SynchronizationOptions())
    );
    Assert.Empty(
      (await handler.Handle(new(CachedOnly: true), default)).Response!.Trucks
    );
    var snapshot = new FleetLocationsResponse
    {
      Trucks = [new() { TruckId = Guid.NewGuid() }],
    };
    await telemetry.GetAsync(_ => Task.FromResult(snapshot), default);
    memory.Remove((FleetTelemetryCache.CacheKey, Company.Amf));
    Assert.Same(
      snapshot,
      (await handler.Handle(new(CachedOnly: true), default)).Response
    );
  }

  [Theory]
  [InlineData(true, 2)]
  [InlineData(false, 2)]
  [InlineData(true, 30001)]
  public async Task WarmReadsAreQueryFreeAndProgressFreshlyValidatesWork(
    bool enabled,
    int pointCount
  )
  {
    await using var fixture = await Database.CreateAsync();
    var db = fixture.Db;
    var truck = new Truck
    {
      Id = Guid.NewGuid(),
      ExternalId = "truck",
      UnitNumber = "1",
      IsActive = true,
    };
    var load = new Dispatch
    {
      Id = Guid.NewGuid(),
      Truck = truck,
      Status = "assigned",
      LoadNumber = 1,
      Stops =
      [
        new()
        {
          Id = Guid.NewGuid(),
          Sequence = 1,
          Job = "Pick Up",
          Latitude = 40,
          Longitude = -80,
          PickedUpAt = DateTime.UtcNow.AddHours(-1),
        },
        new()
        {
          Id = Guid.NewGuid(),
          Sequence = 2,
          Job = "Drop Off",
          Latitude = 40,
          Longitude = -79,
        },
      ],
    };
    db.Dispatches.Add(load);
    await db.SaveChangesAsync();
    load = await db
      .Dispatches.AsNoTracking()
      .Include(x => x.Stops)
      .SingleAsync();
    var profile = new TruckRouteProfile();
    var route = new TruckRoute
    {
      Miles = 100,
      Seconds = 6000,
      Points = [new(40, -80), new(40, -79)],
      Legs = [new(100, 6000, [new(40, -80), new(40, -79)])],
    };
    if (pointCount > 2)
    {
      route.Points = Enumerable
        .Range(0, pointCount)
        .Select(i => new RoutePoint(40, -80 + i / (double)(pointCount - 1)))
        .ToList();
      route.Legs = [new(100, 6000, route.Points)];
    }
    var plan = new RoutePlan
    {
      Id = Guid.NewGuid(),
      DispatchId = load.Id,
      TruckId = truck.Id,
      Route = route,
      Profile = profile,
      Stops = load
        .Stops.Select(x => new PlanStop(
          x.Id,
          "",
          "",
          x.Sequence,
          new((double)x.Latitude!, (double)x.Longitude!)
        ))
        .ToList(),
    };
    db.DispatchRoutePlans.Add(
      new()
      {
        Id = plan.Id,
        DispatchId = load.Id,
        TruckId = truck.Id,
        InputHash = RoutePlanInputs.Hash(load, profile),
        PlanJson = JsonSerializer.Serialize(plan, RoutingJson.Options),
      }
    );
    await db.SaveChangesAsync();
    db.ChangeTracker.Clear();
    using var memory = new MemoryCache(new MemoryCacheOptions());
    using var cache = new ReadCache(
      Options.Create(new SynchronizationOptions())
    );
    var sender = new Sender();
    sender.Fleet.Trucks =
    [
      new()
      {
        TruckId = truck.Id,
        Latitude = 40,
        Longitude = -79.5m,
        UpdatedAt = DateTime.UtcNow,
      },
    ];
    using var displays = new RouteDisplayCache(cache);
    using var services = new PlanningTestServices(
      db,
      new NoRouter(),
      sender,
      cache
    );
    var routes = services.Routes;
    await routes.GetAsync(load.Id, default);
    await routes.GetAsync(load, default, displayOnly: true);
    var options = Options.Create(
      new SynchronizationOptions { Enabled = enabled }
    );
    using var wake = new PlanningRefreshSignal();
    var queue = new PlanningRefreshQueue(
      new PlanningRefreshStore(db),
      wake,
      memory,
      options,
      TimeProvider.System
    );
    var browser = new PlanningReadService(
      routes,
      services.PlanningInputs,
      queue,
      sender,
      options,
      services.Eta,
      services.FuelPlans
    );
    Assert.NotNull(
      (await browser.ForDispatchAsync(load.Id, default)).State?.Plan
    );
    var reads = fixture.Counter.Reads;
    for (var i = 0; i < 20; i++)
    {
      var state = await routes.GetAsync(load.Id, default);
      Assert.Equal(50, state.Progress!.RemainingMiles!.Value, 3);
    }
    for (var i = 0; i < 20; i++)
      Assert.NotNull(
        (await browser.ForDispatchAsync(load.Id, default)).State?.Plan
      );
    Assert.Equal(reads, fixture.Counter.Reads);
    Assert.Equal(
      enabled ? 0 : 1,
      await db.PlanningRefreshRequests.CountAsync()
    );
    var beforeCapture = fixture.Counter.Reads;
    await services.PlanningInputs.ReadFreshAsync(truck.Id, default);
    var captureReads = fixture.Counter.Reads - beforeCapture;
    Assert.True(captureReads > 0);
    await routes.AdvanceAutomaticallyAsync(load.Id, default);
    await routes.GetAsync(load.Id, default);
    reads = fixture.Counter.Reads;
    await routes.AdvanceAutomaticallyAsync(load.Id, default);
    await routes.GetAsync(load.Id, default);
    // A profile warmed by the first capture may eliminate one later read.
    Assert.InRange(
      fixture.Counter.Reads,
      reads + 2 * captureReads - 1,
      reads + 2 * captureReads
    );
  }

  [Fact]
  public async Task SlowCacheMissDoesNotBlockUnrelatedData()
  {
    using var memory = new MemoryCache(new MemoryCacheOptions());
    using var cache = new ReadCache(
      Options.Create(new SynchronizationOptions())
    );
    var started = new TaskCompletionSource();
    var release = new TaskCompletionSource();
    var first = cache.GetAsync(
      "slow",
      "key",
      async () =>
      {
        started.SetResult();
        await release.Task;
        return 1;
      }
    );
    await started.Task;
    var key = Enumerable
      .Range(0, 1000)
      .Select(x => x.ToString())
      .First(x =>
        (uint)StringComparer.Ordinal.GetHashCode($"read:fast:0:{x}") % 64
        != (uint)StringComparer.Ordinal.GetHashCode("read:slow:0:key") % 64
      );
    try
    {
      Assert.Equal(
        2,
        await cache
          .GetAsync("fast", key, () => Task.FromResult(2))
          .WaitAsync(TimeSpan.FromSeconds(2))
      );
    }
    finally
    {
      release.SetResult();
      await first;
    }
  }

  [Fact]
  public async Task ReadCacheSharesConcurrentLoadsAndDoesNotLeakMutations()
  {
    using var memory = new MemoryCache(new MemoryCacheOptions());
    using var cache = new ReadCache(
      Options.Create(new SynchronizationOptions())
    );
    var calls = 0;
    async Task<List<string>> Load()
    {
      Interlocked.Increment(ref calls);
      await Task.Yield();
      return ["saved"];
    }
    var results = await Task.WhenAll(
      Enumerable.Range(0, 10).Select(_ => cache.GetAsync("test", "key", Load))
    );
    Assert.Equal(1, calls);
    results[0].Clear();
    Assert.Equal(
      "saved",
      Assert.Single(await cache.GetAsync("test", "key", Load))
    );
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
    Assert.Null(job.Error);
    Assert.Equal(0, job.Failures);
  }

  private sealed class DispatchProvider(ExternalDispatch source)
    : IDispatchProvider
  {
    public string Key => DispatchImportTestData.Key;
    public string DisplayName => DispatchImportTestData.DisplayName;

    public Task<IReadOnlyList<ExternalDispatch>> GetDispatchesAsync(
      CancellationToken ct = default
    ) => Task.FromResult(DispatchImportTestData.Identify([source]));

    public Task<IReadOnlyList<ExternalDispatch>> GetDispatchesAsync(
      DateOnly from,
      DateOnly to,
      CancellationToken ct = default
    ) => GetDispatchesAsync(ct);
  }

  private sealed class FeedProvider : IFleetTelemetryFeedProvider
  {
    public ConcurrentQueue<string?> Cursors = new();

    public Task<TelemetryFeed> GetFeedAsync(
      string? cursor,
      CancellationToken ct
    )
    {
      Cursors.Enqueue(cursor);
      var now = DateTime.UtcNow;
      return Task.FromResult(
        new TelemetryFeed(
          [
            new(
              "truck",
              new()
              {
                ExternalId = "truck",
                Latitude = 40,
                Longitude = -80,
                UpdatedAt = now,
              },
              "On",
              now,
              50,
              now,
              22.5m,
              now.AddMinutes(-1)
            ),
          ],
          $"cursor-{Cursors.Count}",
          false
        )
      );
    }
  }

  internal sealed class Sender : ISender
  {
    public FleetLocationsResponse Fleet { get; } = new();
    public int CatalogCalls,
      AssignmentCalls,
      DispatchCalls;

    public Task<TResponse> Send<TResponse>(
      IRequest<TResponse> request,
      CancellationToken ct = default
    )
    {
      object result = request switch
      {
        GetFleetLocationsQuery => RequestResponse<FleetLocationsResponse>.Ok(
          Fleet
        ),
        GetDispatchBoardQuery => RequestResponse<
          PaginatedList<TruckDispatchBoardResponse>
        >.Ok(
          new()
          {
            Items = [],
            Page = 1,
            PageSize = 100,
            TotalCount = 0,
          }
        ),
        SyncFleetCommand => RequestResponse<int>.Ok(
          Interlocked.Increment(ref CatalogCalls)
        ),
        SyncAssignmentsCommand => RequestResponse<int>.Ok(
          Interlocked.Increment(ref AssignmentCalls)
        ),
        SyncDispatchesCommand => RequestResponse<int>.Ok(
          Interlocked.Increment(ref DispatchCalls)
        ),
        _ => throw new NotSupportedException(),
      };
      return Task.FromResult((TResponse)result);
    }

    public Task Send<TRequest>(TRequest request, CancellationToken ct = default)
      where TRequest : IRequest => throw new NotSupportedException();

    public Task<object?> Send(object request, CancellationToken ct = default) =>
      throw new NotSupportedException();

    public IAsyncEnumerable<TResponse> CreateStream<TResponse>(
      IStreamRequest<TResponse> request,
      CancellationToken ct = default
    ) => throw new NotSupportedException();

    public IAsyncEnumerable<object?> CreateStream(
      object request,
      CancellationToken ct = default
    ) => throw new NotSupportedException();
  }

  private sealed class NoRouter : IRoutingProvider
  {
    public bool IsConfigured => true;

    public Task<TruckRoute> CalculateAsync(
      IReadOnlyList<RoutePoint> points,
      TruckRouteProfile profile,
      CancellationToken ct
    ) => throw new Exception("Unexpected routing request");

    public Task<RoutePoint> GeocodeAsync(
      string address,
      CancellationToken ct
    ) => throw new Exception("Unexpected geocoding request");
  }

  private sealed class Counter : DbCommandInterceptor
  {
    public int Reads;

    public override ValueTask<
      InterceptionResult<DbDataReader>
    > ReaderExecutingAsync(
      DbCommand command,
      CommandEventData eventData,
      InterceptionResult<DbDataReader> result,
      CancellationToken ct = default
    )
    {
      Interlocked.Increment(ref Reads);
      return ValueTask.FromResult(result);
    }
  }

  private sealed class Database : IAsyncDisposable
  {
    public required AppDbContext Db;
    public required string Path;
    public Counter Counter = new();

    public static async Task<Database> CreateAsync()
    {
      var path = FilePath.Combine(
        FilePath.GetTempPath(),
        $"pulsartms-sync-{Guid.NewGuid():N}.db"
      );
      var counter = new Counter();
      var db = new AppDbContext(
        new DbContextOptionsBuilder<AppDbContext>()
          .UseSqlite($"Data Source={path}")
          .AddInterceptors(counter)
          .Options
      );
      await db.Database.EnsureCreatedAsync();
      return new()
      {
        Db = db,
        Path = path,
        Counter = counter,
      };
    }

    public async ValueTask DisposeAsync()
    {
      await Db.DisposeAsync();
      File.Delete(Path);
    }
  }

  private sealed class NoRecordedPositions : ITruckLocationStore
  {
    public Task<IReadOnlyList<TruckLocation>> ReadAsync(CancellationToken ct) =>
      Task.FromResult<IReadOnlyList<TruckLocation>>([]);

    public Task WriteAsync(
      IReadOnlyList<TruckLocation> trucks,
      CancellationToken ct
    ) => Task.CompletedTask;
  }
}
