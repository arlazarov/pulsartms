using Application.Caching;
using Application.Features.Dispatch.Commands.SyncDispatche;
using Application.Features.Dispatch.Models;
using Application.Features.Dispatch.Queries;
using Application.Features.Fleet.Commands.SyncFleet;
using Application.Features.Fleet.Interfaces;
using Application.Features.Fleet.Models;
using Application.Features.Fleet.Queries.GetFleetLocations;
using Application.Features.Fleet.Services;
using Application.Features.Fuel.Interfaces;
using Application.Features.Fuel.Models;
using Application.Features.Fuel.Services;
using Application.Features.Routing.Algorithms;
using Application.Features.Routing.Background;
using Application.Features.Routing.Commands;
using Application.Features.Routing.Exceptions;
using Application.Features.Routing.Interfaces;
using Application.Features.Routing.Models;
using Application.Features.Routing.Options;
using Application.Features.Routing.Services.Deadheads;
using Application.Features.Routing.Services.FuelPlanning;
using Application.Features.Routing.Services.Routes;
using Application.Features.Synchronization.Interfaces;
using Application.Features.Synchronization.Models;
using Application.Features.Synchronization.Options;
using Application.Features.Synchronization.Services;
using Application.Interfaces;
using Application.Models;
using Infrastructure.Persistence;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Server.Tests.Support;

namespace Server.Tests.Synchronization;

[Trait("Category", "Synchronization")]
[Trait("Kind", "Integration")]
public sealed class SynchronizationCadenceTests
{
  [Theory]
  [InlineData(false, false)]
  [InlineData(true, false)]
  [InlineData(false, true)]
  public async Task FailedFuelRefreshRetriesWithoutBlockingUpcomingRoutes(
    bool exchangeRateFails,
    bool publicationBusy
  )
  {
    var truckId = Guid.NewGuid();
    var dispatchId = Guid.NewGuid();
    var nextId = Guid.NewGuid();
    var sender = new Sender
    {
      PlanningResult = new(
        truckId,
        dispatchId,
        1,
        new(
          new(),
          new() { TruckId = truckId, DispatchId = dispatchId },
          null,
          50,
          DateTime.UtcNow,
          true
        ),
        null
      ),
      PlanningLoads =
      [
        new() { Id = dispatchId, TruckId = truckId },
        new() { Id = nextId, TruckId = truckId },
      ],
    };
    var fuel = new FuelStore(truckId, dispatchId);
    var options = Options.Create(
      new SynchronizationOptions
      {
        Enabled = true,
        HighFrequencyLocations = false,
        CatalogSeconds = 3600,
        AssignmentsSeconds = 3600,
        DispatchSeconds = 3600,
        PlanningSeconds = 3600,
        RetrySeconds = 60,
        UpcomingRoutesPerTruck = 1,
      }
    );
    var services = new ServiceCollection().AddMemoryCache();
    var exchange = AddExchangeRates(services, exchangeRateFails);
    var retryAt = DateTime.UtcNow.AddMinutes(2);
    if (publicationBusy)
      exchange.Read = _ => throw new RoutePlanningException("Busy", retryAt);
    services.AddSingleton<IOptions<SynchronizationOptions>>(options);
    services.AddSingleton(Options.Create(new RoutePreparationOptions()));
    services.AddSingleton<TimeProvider>(TimeProvider.System);
    services.AddSingleton<RoutePreparationQueue>();
    services.AddSingleton<ReadCache>();
    services.AddScoped<FleetCache>();
    services.AddScoped<IAppDbContext>(_ => new AppDbContext(
      new DbContextOptionsBuilder<AppDbContext>()
        .UseSqlite("Data Source=:memory:")
        .Options
    ));
    services.AddSingleton<ISynchronizationStore>(new Store());
    services.AddSingleton<ISender>(sender);
    services.AddSingleton<ITruckFuelPlanStore>(fuel);
    services.AddScoped<FuelPriceRefreshService>();
    services.AddScoped<ISavedRoadValidation>(provider =>
    {
      var db = (AppDbContext)provider.GetRequiredService<IAppDbContext>();
      return new SavedRoadValidation(
        db,
        new NextLoadRouteReader(db),
        new SavedRoutePlanReader(db),
        new ExecutionReadScope(db)
      );
    });
    services.AddSingleton<IFuelWorkInputsReader>(sender);
    services.AddScoped<IFuelSavedInputsValidation>(provider =>
    {
      var db = (AppDbContext)provider.GetRequiredService<IAppDbContext>();
      return new FuelSavedInputsValidation(
        provider.GetRequiredService<ISavedRoadValidation>(),
        new DeadheadHistoryService(
          db,
          new DeadheadHistoryReader(db),
          new ExecutionReadScope(db)
        ),
        new ExecutionReadScope(db)
      );
    });
    services.AddSingleton<IFleetTelemetryFeedProvider>(new Feed());
    await using var provider = services.BuildServiceProvider();
    provider
      .GetRequiredService<IMemoryCache>()
      .Set(
        FleetCache.CacheKey,
        Array.Empty<FleetTruckInfo>(),
        TimeSpan.FromHours(1)
      );
    var operation = new FleetSynchronizationOperation(
      provider.GetRequiredService<IServiceScopeFactory>(),
      options,
      DispatchImportTestData.Options,
      new ServerTelemetry(),
      NullLogger<FleetSynchronizationOperation>.Instance
    );
    using var cancellation = new CancellationTokenSource();
    var startedAt = DateTime.UtcNow;
    var pending = operation.RunAsync(cancellation.Token);
    try
    {
      await sender.UpcomingPrepared.Task.WaitAsync(TimeSpan.FromSeconds(5));
      using var bound = new CancellationTokenSource(TimeSpan.FromSeconds(5));
      while (
        operation.Status.Jobs.GetValueOrDefault($"truck:{truckId}")?.LastSuccess
          is null
        || operation.Status.Jobs.GetValueOrDefault("fuel-exchange-rate")
          is not { NextRun: var scheduled }
        || scheduled == default
      )
        await Task.Delay(10, bound.Token);
      var status = operation.Status;
      var failed = status.Jobs[$"fuel:{truckId}"];
      Assert.Equal(1, failed.Failures);
      Assert.Null(failed.LastSuccess);
      Assert.Equal(nameof(RoutePlanningException), failed.Error);
      Assert.True(failed.NextRun >= startedAt.AddSeconds(60));
      var truck = status.Jobs[$"truck:{truckId}"];
      Assert.Equal(0, truck.Failures);
      Assert.Null(truck.Error);
      Assert.NotNull(truck.LastSuccess);
      Assert.Equal(1, sender.FuelAttempts);
      var exchangeJob = status.Jobs["fuel-exchange-rate"];
      Assert.Equal(exchangeRateFails ? 1 : 0, exchangeJob.Failures);
      Assert.Equal(
        exchangeRateFails ? nameof(HttpRequestException) : null,
        exchangeJob.Error
      );
      Assert.Equal(
        exchangeRateFails || publicationBusy,
        exchangeJob.LastSuccess is null
      );
      if (publicationBusy)
        Assert.Equal(retryAt, exchangeJob.NextRun);
      else
        Assert.True(
          exchangeJob.NextRun
            >= startedAt.AddSeconds(exchangeRateFails ? 60 : 3600)
        );
      Assert.Equal(1, exchange.Calls);
      Assert.NotNull(status.Jobs["catalog"].LastSuccess);
      Assert.NotNull(status.Jobs["assignments"].LastSuccess);
      var upcoming = Assert.Single(sender.Upcoming);
      Assert.Equal(nextId, upcoming.DispatchId);
      Assert.Equal(truckId, upcoming.TruckId);
      var queue = provider.GetRequiredService<RoutePreparationQueue>();
      Assert.Equal(1, queue.PendingCount);
      Assert.Equal(truckId, queue.Identity(nextId).TruckId);
    }
    finally
    {
      await cancellation.CancelAsync();
      await pending.WaitAsync(TimeSpan.FromSeconds(5));
    }
  }

  [Fact]
  public async Task LocationsPublishWhileFeedIsBlockedAndRetainItsCursor()
  {
    var feed = new BlockedFeed();
    var stream = new StreamProvider();
    var options = Options.Create(
      new SynchronizationOptions
      {
        Enabled = true,
        HighFrequencyLocations = true,
        CatalogSeconds = 3600,
        DispatchSeconds = 3600,
        PlanningSeconds = 3600,
      }
    );
    var services = new ServiceCollection().AddMemoryCache();
    AddExchangeRates(services);
    services.AddSingleton<IOptions<SynchronizationOptions>>(options);
    services.AddSingleton<ReadCache>();
    services.AddSingleton<TimeProvider>(TimeProvider.System);
    services.AddSingleton<FleetLocationStream>();
    services.AddScoped<FleetCache>();
    services.AddScoped<IAppDbContext>(_ => new AppDbContext(
      new DbContextOptionsBuilder<AppDbContext>()
        .UseSqlite("Data Source=:memory:")
        .Options
    ));
    services.AddSingleton<ISynchronizationStore>(new Store());
    services.AddSingleton<ISender>(new Sender());
    services.AddSingleton<IFleetTelemetryFeedProvider>(feed);
    services.AddSingleton<IFleetTelemetryProvider>(stream);
    await using var provider = services.BuildServiceProvider();
    provider
      .GetRequiredService<IMemoryCache>()
      .Set(
        FleetCache.CacheKey,
        new FleetTruckInfo[]
        {
          new()
          {
            TruckId = Guid.NewGuid(),
            TruckExternalId = "truck",
            UnitNumber = "11006",
            IsActive = true,
          },
        },
        TimeSpan.FromHours(1)
      );
    var telemetry = new ServerTelemetry();
    var operation = new FleetSynchronizationOperation(
      provider.GetRequiredService<IServiceScopeFactory>(),
      options,
      DispatchImportTestData.Options,
      telemetry,
      NullLogger<FleetSynchronizationOperation>.Instance
    );
    using var cancellation = new CancellationTokenSource();
    var pending = operation.RunAsync(cancellation.Token);
    try
    {
      await feed.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
      using var bound = new CancellationTokenSource(TimeSpan.FromSeconds(5));
      while (telemetry.Current?.Trucks.SingleOrDefault()?.Latitude != 35)
        await Task.Delay(10, bound.Token);
      Assert.False(feed.Completed);
      Assert.Equal(
        "New address",
        telemetry.Current!.Trucks[0].FormattedLocation
      );
      Assert.Null(operation.Status.Jobs["telemetry"].LastSuccess);
      Assert.True(operation.Status.Active);
    }
    finally
    {
      await cancellation.CancelAsync();
      await pending.WaitAsync(TimeSpan.FromSeconds(5));
    }
  }

  private sealed class BlockedFeed : IFleetTelemetryFeedProvider
  {
    public TaskCompletionSource Started { get; } =
      new(TaskCreationOptions.RunContinuationsAsynchronously);
    public bool Completed;

    public async Task<TelemetryFeed> GetFeedAsync(
      string? cursor,
      CancellationToken ct
    )
    {
      Started.TrySetResult();
      await Task.Delay(Timeout.Infinite, ct);
      Completed = true;
      return new([], "unused", false);
    }
  }

  private sealed class StreamProvider : IFleetTelemetryProvider
  {
    public Task<IReadOnlyList<VehicleTelemetry>> GetVehicleTelemetryAsync(
      CancellationToken ct = default
    ) => throw new InvalidOperationException("No snapshot read expected.");

    public Task<VehicleLocationStream> GetLocationStreamAsync(
      IReadOnlyCollection<string> ids,
      DateTime start,
      DateTime end,
      string? cursor = null,
      CancellationToken ct = default
    ) =>
      Task.FromResult(
        new VehicleLocationStream
        {
          Data =
          [
            new()
            {
              ExternalId = "truck",
              Latitude = 35,
              Longitude = -99,
              UpdatedAt = end,
              FormattedLocation = "New address",
            },
          ],
          HasNextPage = false,
        }
      );
  }

  [Fact]
  public async Task SlowLaterCatalogDoesNotDelayDueTorqueWorkOrChangeLeaseOwnership()
  {
    var sender = new Sender();
    var store = new Store();
    var options = Options.Create(
      new SynchronizationOptions
      {
        Enabled = true,
        CatalogSeconds = 0,
        DispatchSeconds = 0,
        HighFrequencyLocations = false,
        AssignmentsSeconds = 3600,
        PlanningSeconds = 3600,
      }
    );
    var services = new ServiceCollection().AddMemoryCache();
    AddExchangeRates(services);
    services.AddSingleton<IOptions<SynchronizationOptions>>(options);
    services.AddSingleton<ReadCache>();
    services.AddScoped<FleetCache>();
    services.AddScoped<IAppDbContext>(_ => new AppDbContext(
      new DbContextOptionsBuilder<AppDbContext>()
        .UseSqlite("Data Source=:memory:")
        .Options
    ));
    services.AddSingleton<ISynchronizationStore>(store);
    services.AddSingleton<ISender>(sender);
    services.AddSingleton<IFleetTelemetryFeedProvider>(new Feed());
    await using var provider = services.BuildServiceProvider();
    provider
      .GetRequiredService<IMemoryCache>()
      .Set(
        FleetCache.CacheKey,
        Array.Empty<FleetTruckInfo>(),
        TimeSpan.FromHours(1)
      );
    var operation = new FleetSynchronizationOperation(
      provider.GetRequiredService<IServiceScopeFactory>(),
      options,
      DispatchImportTestData.Options,
      new ServerTelemetry(),
      NullLogger<FleetSynchronizationOperation>.Instance
    );
    using var cancellation = new CancellationTokenSource();
    var pending = operation.RunAsync(cancellation.Token);
    try
    {
      await sender.SecondCatalog.Task.WaitAsync(TimeSpan.FromSeconds(10));
      await sender.SecondDispatch.Task.WaitAsync(TimeSpan.FromSeconds(2));
      Assert.False(sender.ReleaseCatalog.Task.IsCompleted);
      Assert.True(sender.IdentityOnlyPlanning);
      Assert.True(operation.Status.Active);
      Assert.Equal(1, store.Acquisitions);
    }
    finally
    {
      await cancellation.CancelAsync();
      sender.ReleaseCatalog.TrySetResult();
      await pending.WaitAsync(TimeSpan.FromSeconds(5));
    }
    Assert.Equal(1, store.Releases);
  }

  private static StubFuelExchangeRateProvider AddExchangeRates(
    IServiceCollection services,
    bool fails = false
  )
  {
    var provider = new StubFuelExchangeRateProvider
    {
      Read = _ =>
        fails
          ? throw new HttpRequestException("Exchange-rate service unavailable.")
          : Task.FromResult(
            new FuelExchangeRate(
              .73m,
              DateOnly.FromDateTime(DateTime.UtcNow),
              DateTime.UtcNow
            )
          ),
    };
    services.AddSingleton<TimeProvider>(TimeProvider.System);
    services.AddSingleton<IFuelExchangeRateStore>(
      new MemoryFuelExchangeRateStore()
    );
    services.AddSingleton<IFuelExchangeRateProvider>(provider);
    services.AddScoped<FuelExchangeRateService>();
    return provider;
  }

  private sealed class Store : ISynchronizationStore
  {
    public int Acquisitions,
      Releases;

    public Task<bool> AcquireAsync(
      string owner,
      DateTime now,
      CancellationToken ct
    )
    {
      Acquisitions++;
      return Task.FromResult(true);
    }

    public Task<bool> RenewAsync(
      string owner,
      DateTime now,
      CancellationToken ct
    ) => Task.FromResult(true);

    public Task<SynchronizationState> ReadAsync(CancellationToken ct) =>
      Task.FromResult(new SynchronizationState());

    public Task SaveAsync(string owner, string json, CancellationToken ct) =>
      Task.CompletedTask;

    public Task ReleaseAsync(string owner, CancellationToken ct)
    {
      Releases++;
      return Task.CompletedTask;
    }
  }

  private sealed class Feed : IFleetTelemetryFeedProvider
  {
    public Task<TelemetryFeed> GetFeedAsync(
      string? cursor,
      CancellationToken ct
    ) => Task.FromResult(new TelemetryFeed([], "cursor", false));
  }

  private sealed class FuelStore(Guid truckId, Guid dispatchId)
    : ITruckFuelPlanStore
  {
    private readonly TruckFuelPlanSnapshot saved = new(
      truckId,
      dispatchId,
      DateTime.UtcNow,
      new() { SelectionVersion = FuelOptimizer.SelectionVersion - 1 },
      [],
      null
    );

    public Task<TruckFuelPlanSnapshot?> ReadAsync(
      Guid id,
      bool includeRoute,
      CancellationToken ct
    ) => Task.FromResult<TruckFuelPlanSnapshot?>(saved);

    public Task<bool> SaveAsync(
      TruckFuelPlanSnapshot snapshot,
      CancellationToken ct
    ) => throw new NotSupportedException();

    public Task<bool> ReplaceAsync(
      TruckFuelPlanSnapshot snapshot,
      DateTime? expected,
      CancellationToken ct
    ) => throw new NotSupportedException();
  }

  private sealed class Sender : ISender, IFuelWorkInputsReader
  {
    private int catalogs,
      dispatches;
    public bool IdentityOnlyPlanning;
    public AutomaticPlanningResult? PlanningResult { get; init; }
    public List<DispatchResponse> PlanningLoads { get; init; } = [];
    public int FuelAttempts;
    public List<PrepareUpcomingPlanningCommand> Upcoming { get; } = [];
    public TaskCompletionSource UpcomingPrepared { get; } =
      new(TaskCreationOptions.RunContinuationsAsynchronously);
    public TaskCompletionSource SecondCatalog { get; } =
      new(TaskCreationOptions.RunContinuationsAsynchronously);
    public TaskCompletionSource SecondDispatch { get; } =
      new(TaskCreationOptions.RunContinuationsAsynchronously);
    public TaskCompletionSource ReleaseCatalog { get; } =
      new(TaskCreationOptions.RunContinuationsAsynchronously);

    public async Task<TResponse> Send<TResponse>(
      IRequest<TResponse> request,
      CancellationToken ct = default
    )
    {
      object result;
      if (request is SyncFleetCommand)
      {
        if (++catalogs == 2)
        {
          SecondCatalog.TrySetResult();
          await ReleaseCatalog.Task.WaitAsync(ct);
        }
        result = RequestResponse<int>.Ok(0);
      }
      else if (request is SyncDispatchesCommand)
      {
        if (++dispatches == 2)
          SecondDispatch.TrySetResult();
        result = RequestResponse<int>.Ok(0);
      }
      else if (request is SyncAssignmentsCommand)
        result = RequestResponse<int>.Ok(0);
      else if (request is GetDispatchBoardQuery board)
      {
        IdentityOnlyPlanning = board.IdentitiesOnly;
        result = RequestResponse<PaginatedList<TruckDispatchBoardResponse>>.Ok(
          new()
          {
            Items = PlanningResult is null
              ? []
              :
              [
                new()
                {
                  TruckId = PlanningResult.TruckId,
                  Dispatches = PlanningLoads,
                },
              ],
            Page = 1,
            PageSize = 100,
          }
        );
      }
      else if (request is PrepareTruckPlanningCommand)
        result = RequestResponse<AutomaticPlanningResult>.Ok(PlanningResult!);
      else if (request is RecalculateFuelPlanCommand)
      {
        FuelAttempts++;
        result = RequestResponse<AutomaticPlanningResult>.Fail(
          "Fuel preparation is temporarily unavailable."
        );
      }
      else if (request is PrepareUpcomingPlanningCommand upcoming)
      {
        Upcoming.Add(upcoming);
        UpcomingPrepared.TrySetResult();
        result = RequestResponse<bool>.Ok(true);
      }
      else
        throw new NotSupportedException();
      return (TResponse)result;
    }

    public Task<FuelWorkInputs> ReadFreshAsync(
      Guid truckId,
      CancellationToken ct
    ) => throw new NotSupportedException();

    public Task<FuelWorkInputs?> ReadDisplayAsync(
      Guid truckId,
      CancellationToken ct
    ) =>
      Task.FromResult<FuelWorkInputs?>(
        FuelWorkFixture.Capture(truckId, PlanningLoads)
      );

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
}
