using System.Data.Common;
using System.Text.Json;
using Application.Caching;
using Application.Features.Eta.Interfaces;
using Application.Features.Eta.Services;
using Application.Features.Synchronization.Options;
using Application.Interfaces;
using Domain.Entities.Dispatch;
using Domain.Entities.Fleet;
using Domain.Models.Eta;
using Domain.Models.Fleet;
using Domain.Models.Routing;
using Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Server.Tests.Eta;

// Stage 4e of docs/architecture/current-work.md: two API processes over
// one database, each with its own ETA memory and read cache, joined only
// by the store and the cache relay. A display read shows the newest
// committed forecast of the plan's work whichever process committed it.
public sealed partial class EtaChainInputTests
{
  // Cold, the other process reads the committed forecast - the whole
  // chain, as its maker's memory holds it - in one query; warm, in none.
  [Fact]
  public async Task AnotherProcessShowsTheCommittedForecastColdThenWarm()
  {
    await using var f = await Fixture.CreateAsync(
      sender: new DispatchTelemetrySender(new())
    );
    await using var b = Process.Create(f);
    await f.Services.Forecasts.RefreshAsync(f.Current.Id, default);
    var made = await Shown(f.Services, f.Services.Forecasts, f.Truck.Id);

    var cold = await b.ShownAsync(f.Truck.Id);
    var coldReads = b.Queries.Take();
    var warm = await b.ShownAsync(f.Truck.Id);

    Assert.NotNull(made);
    Assert.Equal(
      Json(f.Services.EtaMemory.Results[f.Current.Id].Value),
      Json((await SavedRoot(f)).Forecast)
    );
    Assert.Empty(b.Services.EtaMemory.Results);
    Assert.Equal(Json(made), Json(cold));
    Assert.Equal(Json(made), Json(warm));
    Assert.Equal(1, coldReads);
    Assert.Equal(0, b.Queries.Take());
  }

  // A commit elsewhere reaches a warm reader through the relay, not by
  // chance: until the relay delivers it the reader keeps what it read,
  // and after it the reader reads again, once.
  [Fact]
  public async Task ACommitElsewhereReachesAWarmReaderThroughTheRelay()
  {
    await using var f = await Fixture.CreateAsync(
      sender: new DispatchTelemetrySender(new())
    );
    await using var a = Process.Of(f);
    await using var b = Process.Create(f);
    await f.Services.Forecasts.RefreshAsync(f.Current.Id, default);
    var first = await b.ShownAsync(f.Truck.Id);
    b.Queries.Take();

    await Recalculate(f);
    var second = await Shown(f.Services, f.Services.Forecasts, f.Truck.Id);
    var beforeRelay = await b.ShownAsync(f.Truck.Id);
    var readsBeforeRelay = b.Queries.Take();
    await a.Relay.RunOnceAsync(default);
    await b.Relay.RunOnceAsync(default);
    var afterRelay = await b.ShownAsync(f.Truck.Id);

    Assert.True(second!.CalculatedAt > first!.CalculatedAt);
    Assert.Equal(first.CalculatedAt, beforeRelay!.CalculatedAt);
    Assert.Equal(0, readsBeforeRelay);
    Assert.Equal(Json(second), Json(afterRelay));
    Assert.Equal(1, b.Queries.Take());
  }

  // A cold read that loaded the older row while the other process
  // committed a newer one and the relay delivered it: the read answers
  // with what it loaded, but does not keep it, so the next read is the
  // newer one.
  [Fact]
  public async Task ARowLoadedAcrossACommitElsewhereIsNotKept()
  {
    await using var f = await Fixture.CreateAsync(
      sender: new DispatchTelemetrySender(new())
    );
    await using var a = Process.Of(f);
    await using var b = Process.Create(f);
    await f.Services.Forecasts.RefreshAsync(f.Current.Id, default);
    var older = await Shown(f.Services, f.Services.Forecasts, f.Truck.Id);
    b.Store.AfterRead = async () =>
    {
      await Recalculate(f);
      await a.Relay.RunOnceAsync(default);
      await b.Relay.RunOnceAsync(default);
    };

    var during = await b.ShownAsync(f.Truck.Id);
    var duringReads = b.Queries.Take();
    var after = await b.ShownAsync(f.Truck.Id);
    var afterReads = b.Queries.Take();
    var newer = await Shown(f.Services, f.Services.Forecasts, f.Truck.Id);

    Assert.Equal(older!.CalculatedAt, during!.CalculatedAt);
    Assert.True(newer!.CalculatedAt > older.CalculatedAt);
    Assert.Equal(Json(newer), Json(after));
    Assert.Equal(1, duringReads);
    Assert.Equal(1, afterReads);
  }

  // The other process calculated first and committed second: the store
  // refuses its older forecast, so its memory never holds it, and both
  // processes show the one the store kept.
  [Fact]
  public async Task AnOlderCalculationTheStoreRefusedIsNeverShown()
  {
    await using var f = await Fixture.CreateAsync(
      sender: new DispatchTelemetrySender(new())
    );
    await using var a = Process.Of(f);
    await using var b = Process.Create(f);
    var interleaved = false;
    b.Publication.BeforeBegin = async () =>
    {
      if (interleaved)
        return;
      interleaved = true;
      await f.Services.Forecasts.RefreshAsync(f.Current.Id, default);
      await a.Relay.RunOnceAsync(default);
      await b.Relay.RunOnceAsync(default);
    };

    await b.Forecasts.RefreshAsync(f.Current.Id, default);
    var kept = await Shown(f.Services, f.Services.Forecasts, f.Truck.Id);
    var elsewhere = await b.ShownAsync(f.Truck.Id);

    Assert.True(interleaved);
    Assert.False(b.Services.EtaMemory.Results.ContainsKey(f.Current.Id));
    Assert.NotNull(kept);
    Assert.Equal(Json(kept), Json(elsewhere));
    Assert.Equal(kept.CalculatedAt, (await SavedRoot(f)).Forecast.CalculatedAt);
  }

  // A row saved before the keys existed cannot be judged against a plan,
  // so a process without its own copy shows no forecast rather than one it
  // cannot vouch for; the next commit writes the keys and every process
  // shows it.
  [Fact]
  public async Task ARowSavedWithoutKeysIsShownOnlyOnceRecommitted()
  {
    await using var f = await Fixture.CreateAsync(
      sender: new DispatchTelemetrySender(new())
    );
    await using var a = Process.Of(f);
    await using var b = Process.Create(f);
    await f.Services.Forecasts.RefreshAsync(f.Current.Id, default);
    await f
      .Db.Set<DispatchEtaForecast>()
      .ExecuteUpdateAsync(s =>
        s.SetProperty(x => x.WorkKey, (string?)null)
          .SetProperty(x => x.RouteKey, (string?)null)
      );

    var withoutKeys = await b.ShownAsync(f.Truck.Id);
    await Recalculate(f);
    await a.Relay.RunOnceAsync(default);
    await b.Relay.RunOnceAsync(default);
    var recommitted = await b.ShownAsync(f.Truck.Id);
    var root = await SavedRoot(f);

    Assert.Null(withoutKeys);
    Assert.NotNull(root.WorkKey);
    Assert.Equal(
      Json(await Shown(f.Services, f.Services.Forecasts, f.Truck.Id)),
      Json(recommitted)
    );
  }

  // Two forecasts calculated at the same instant: the store kept one and
  // refused the other, whose process may still hold it. Every process
  // shows the one the store kept, the maker's own copy included.
  [Fact]
  public async Task EveryProcessShowsTheStoredOneOfTwoCalculatedTogether()
  {
    await using var f = await Fixture.CreateAsync(
      sender: new DispatchTelemetrySender(new())
    );
    await using var b = Process.Create(f);
    await f.Services.Forecasts.RefreshAsync(f.Current.Id, default);
    var remembered = f.Services.EtaMemory.Results[f.Current.Id].Value;
    var stored = Chain(remembered, f);
    await Store(f, stored);

    var maker = await Shown(f.Services, f.Services.Forecasts, f.Truck.Id);
    var other = await b.ShownAsync(f.Truck.Id);

    Assert.Equal(remembered.CalculatedAt, stored.CalculatedAt);
    Assert.NotEqual(Json(remembered), Json(stored));
    Assert.Equal(Json(stored), Json(maker));
    Assert.Equal(Json(stored), Json(other));
  }

  // The root's row holds the whole chain; the board shows the root load
  // its own part, as it did when the row held only that part - and a row
  // of that older shape reads the same.
  [Fact]
  public async Task TheBoardShowsTheRootLoadItsPartOfTheChain()
  {
    await using var f = await Fixture.CreateAsync(
      sender: new DispatchTelemetrySender(new())
    );
    await f.Services.Forecasts.RefreshAsync(f.Current.Id, default);
    var chain = Chain((await SavedRoot(f)).Forecast, f);
    await Store(f, chain);

    var loads = f.Ordered;
    await f.Services.Forecasts.PopulateAsync(loads, default);
    var shown = loads.Single(x => x.Id == f.Current.Id).Eta!;
    await Store(f, EtaForecastService.Filter(chain, f.Current.Id));
    var older = f.Ordered;
    await f.Services.Forecasts.PopulateAsync(older, default);

    var stop = Assert.Single(shown.Stops);
    Assert.Equal(f.Current.Id, stop.DispatchId);
    Assert.Empty(shown.PendingDispatches);
    Assert.Null(shown.UnavailableReason);
    Assert.False(shown.RouteUpdatePending);
    Assert.Equal(
      Json(shown),
      Json(older.Single(x => x.Id == f.Current.Id).Eta)
    );
  }

  // A chain's forecast with a stop of each load and a later load pending,
  // as the same calculation.
  private static DispatchEta Chain(DispatchEta calculated, Fixture f) =>
    calculated with
    {
      Stops =
      [
        Stop(f.Current.Stops[^1].Id, f.Current.Id),
        Stop(f.Next.Stops[0].Id, f.Next.Id),
      ],
      UnavailableReason = null,
      RouteUpdatePending = false,
      PendingDispatches = new Dictionary<Guid, string>
      {
        [Guid.NewGuid()] = "waiting for its road",
      },
    };

  private static StopEta Stop(Guid stopId, Guid dispatchId) =>
    new(stopId, DateTimeOffset.UtcNow.AddHours(1), "Etc/UTC", null, null, 60, 0)
    {
      DispatchId = dispatchId,
    };

  // Rewrites the root's saved forecast in place: same calculation, keys
  // and row.
  private static Task Store(Fixture f, DispatchEta forecast) =>
    f
      .Db.Set<DispatchEtaForecast>()
      .Where(x => x.DispatchId == f.Current.Id)
      .ExecuteUpdateAsync(s =>
        s.SetProperty(
          x => x.ForecastJson,
          JsonSerializer.Serialize(
            forecast,
            new JsonSerializerOptions(JsonSerializerDefaults.Web)
          )
        )
      );

  // A real calculation, not a row written by hand: the driver's hours and
  // a fresh position on the road give the chain a forecast with stops.
  // It is committed by one process, and another shows the same forecast
  // - every stop, the pending later loads - read from the store in one
  // query; the board shows the root load its own part of it.
  [Fact]
  public async Task AForecastWithStopsCommittedHereIsShownElsewhereIntact()
  {
    var fleet = Positioned();
    await using var f = await Fixture.CreateAsync(
      sender: new DispatchTelemetrySender(fleet)
    );
    fleet.Trucks[0].TruckId = f.Truck.Id;
    await GiveHoursAsync(f);
    await using var b = Process.Create(f, fleet);

    await f.Services.Forecasts.RefreshAsync(f.Current.Id, default);
    var made = await Shown(f.Services, f.Services.Forecasts, f.Truck.Id);
    var elsewhere = await b.ShownAsync(f.Truck.Id);
    var reads = b.Queries.Take();
    var loads = f.Ordered;
    await f.Services.Forecasts.PopulateAsync(loads, default);
    var board = loads.Single(x => x.Id == f.Current.Id).Eta!;

    Assert.NotNull(made);
    Assert.Null(made.UnavailableReason);
    Assert.Contains(made.Stops, x => x.DispatchId == f.Current.Id);
    Assert.Contains(made.Stops, x => x.DispatchId == f.Next.Id);
    Assert.Empty(b.Services.EtaMemory.Results);
    Assert.Equal(Json(made), Json(elsewhere));
    Assert.Equal(1, reads);
    Assert.Equal(
      Json(EtaForecastService.Filter(made, f.Current.Id)),
      Json(board)
    );
  }

  private static FleetLocationsResponse Positioned() =>
    new()
    {
      Trucks =
      [
        new()
        {
          // Named once the fixture made the truck.
          Latitude = 35,
          Longitude = -80.9m,
          Speed = 55,
          EngineState = "On",
          UpdatedAt = DateTime.UtcNow,
          ObservedAt = DateTime.UtcNow,
        },
      ],
    };

  // A driver on the truck and its loads, with a fresh reading of every
  // clock.
  private static async Task GiveHoursAsync(Fixture f)
  {
    var driver = new Driver
    {
      Id = Guid.NewGuid(),
      ExternalId = "eta-driver",
      Name = "ETA Driver",
      IsActive = true,
    };
    f.Db.Drivers.Add(driver);
    var truck = await f.Db.Trucks.SingleAsync(x => x.Id == f.Truck.Id);
    truck.DriverId = driver.Id;
    await f.Db.SaveChangesAsync();
    // The chain is one driver's run: the loads' stops name the same one.
    await f
      .Db.Set<DispatchStop>()
      .ExecuteUpdateAsync(x => x.SetProperty(s => s.DriverId, driver.Id));
    f.Db.ChangeTracker.Clear();
    f.Services.Reads.Invalidate("dispatch");
    f.Services.Hos.Clocks[driver.ExternalId] = new()
    {
      DriveMs = (long)TimeSpan.FromHours(8).TotalMilliseconds,
      ShiftMs = (long)TimeSpan.FromHours(11).TotalMilliseconds,
      CycleMs = (long)TimeSpan.FromHours(50).TotalMilliseconds,
      BreakMs = (long)TimeSpan.FromHours(6).TotalMilliseconds,
      CurrentDutyStatus = "driving",
      UpdatedAt = DateTime.UtcNow,
    };
  }

  // The next refresh calculates anew instead of reusing the result.
  private static async Task Recalculate(Fixture f)
  {
    var memory = f.Services.EtaMemory;
    memory.Results[f.Current.Id] = memory.Results[f.Current.Id] with
    {
      Signature = "earlier",
    };
    await f.Services.Forecasts.RefreshAsync(f.Current.Id, default);
  }

  private static async Task<EtaForecastSnapshot> SavedRoot(Fixture f) =>
    Assert.Single(
      await new EtaForecastStore(
        f.Db,
        NullLogger<EtaForecastStore>.Instance
      ).ReadAsync([f.Current.Id], default)
    );

  // The prepared plan a display read is asked about.
  private static async Task<RoutePlanningState> Prepared(
    PlanningTestServices services,
    Guid truckId
  )
  {
    var description = await services.EtaInputs.DescribeAsync(truckId, default);
    return await services.Routes.GetAsync(
      description!.Loads[0],
      default,
      cachedTelemetryOnly: true
    );
  }

  private static async Task<DispatchEta?> Shown(
    PlanningTestServices services,
    EtaForecastService forecasts,
    Guid truckId
  ) =>
    (
      await forecasts.ReadForDisplayAsync(
        [await Prepared(services, truckId)],
        default
      )
    )[0];

  private static string Json(DispatchEta? value) =>
    JsonSerializer.Serialize(value);

  // A second process: its own context, ETA memory, read cache and relay on
  // the same database. The fixture's services are the first.
  private sealed class Process : IAsyncDisposable
  {
    private readonly ServiceProvider relayServices;
    private readonly AppDbContext? db;
    private readonly ReadCache? reads;

    private Process(
      ServiceProvider relayServices,
      CacheInvalidationRelay relay,
      AppDbContext? db,
      ReadCache? reads
    )
    {
      this.relayServices = relayServices;
      this.db = db;
      this.reads = reads;
      Relay = relay;
    }

    public CacheInvalidationRelay Relay { get; }
    public PlanningTestServices Services { get; private init; } = null!;
    public EtaForecastService Forecasts { get; private init; } = null!;
    public HookedStore Store { get; private init; } = null!;
    public PublicationProbe Publication { get; private init; } = null!;
    public ForecastQueries Queries { get; private init; } = null!;

    public static Process Of(Fixture f)
    {
      var relays = Relays(f);
      return new(relays, NewRelay(relays, f.Services.Reads), null, null);
    }

    public static Process Create(
      Fixture f,
      FleetLocationsResponse? fleet = null
    )
    {
      var queries = new ForecastQueries();
      var db = new AppDbContext(
        new DbContextOptionsBuilder<AppDbContext>()
          .UseSqlite(f.Connection)
          .AddInterceptors(queries)
          .UseApplicationServiceProvider(
            new ServiceCollection()
              .AddSingleton<ICurrentCompany>(f.Company)
              .BuildServiceProvider()
          )
          .Options
      );
      var reads = new ReadCache(
        Options.Create(new SynchronizationOptions()),
        f.Company
      );
      var publication = new PublicationProbe(db);
      var services = new PlanningTestServices(
        db,
        sender: new DispatchTelemetrySender(fleet ?? new()),
        reads: reads,
        publicationScope: publication
      );
      var store = new HookedStore(
        new EtaForecastStore(db, NullLogger<EtaForecastStore>.Instance)
      );
      var relays = Relays(f);
      return new(relays, NewRelay(relays, reads), db, reads)
      {
        Services = services,
        Publication = publication,
        Queries = queries,
        Store = store,
        Forecasts = new(
          services.EtaInputs,
          store,
          services.EtaMemory,
          services.Eta,
          services.Routes,
          services.Publication,
          reads,
          NullLogger<EtaForecastService>.Instance
        ),
      };
    }

    // Counted from the prepared plan on, so only the display read's own
    // queries are counted.
    public async Task<DispatchEta?> ShownAsync(Guid truckId)
    {
      var state = await Prepared(Services, truckId);
      Queries.Take();
      return (await Forecasts.ReadForDisplayAsync([state], default))[0];
    }

    private static ServiceProvider Relays(Fixture f) =>
      new ServiceCollection()
        .AddDbContext<AppDbContext>(x => x.UseSqlite(f.Connection))
        .AddScoped<IAppDbContext>(x => x.GetRequiredService<AppDbContext>())
        .BuildServiceProvider();

    private static CacheInvalidationRelay NewRelay(
      ServiceProvider services,
      ReadCache reads
    ) =>
      new(
        reads,
        services.GetRequiredService<IServiceScopeFactory>(),
        Options.Create(new SynchronizationOptions()),
        TimeProvider.System,
        NullLogger<CacheInvalidationRelay>.Instance
      );

    public async ValueTask DisposeAsync()
    {
      if (db is not null)
      {
        Services.Dispose();
        reads!.Dispose();
        await db.DisposeAsync();
      }
      await relayServices.DisposeAsync();
    }
  }

  // Runs a step once, right after the first read of saved forecasts
  // returned: the point between a load and its caching.
  private sealed class HookedStore(IEtaForecastStore inner) : IEtaForecastStore
  {
    public Func<Task>? AfterRead { get; set; }

    public async Task<IReadOnlyList<EtaForecastSnapshot>> ReadAsync(
      IReadOnlyCollection<Guid> dispatchIds,
      CancellationToken ct
    )
    {
      var read = await inner.ReadAsync(dispatchIds, ct);
      if (AfterRead is { } after)
      {
        AfterRead = null;
        await after();
      }
      return read;
    }

    public Task<IReadOnlyList<EtaForecastSnapshot>> ReadExecutionLegsAsync(
      IReadOnlyCollection<Guid> executionLegIds,
      CancellationToken ct
    ) => inner.ReadExecutionLegsAsync(executionLegIds, ct);

    public Task<bool> SaveAsync(
      IReadOnlyCollection<EtaForecastSnapshot> snapshots,
      CancellationToken ct
    ) => inner.SaveAsync(snapshots, ct);
  }

  // Reads of saved forecasts, counted where they reach the database.
  private sealed class ForecastQueries : DbCommandInterceptor
  {
    private int count;

    public int Take() => Interlocked.Exchange(ref count, 0);

    public override ValueTask<DbDataReader> ReaderExecutedAsync(
      DbCommand command,
      CommandExecutedEventData eventData,
      DbDataReader result,
      CancellationToken cancellationToken = default
    )
    {
      if (
        command.CommandText.Contains(
          "\"DispatchEtaForecasts\"",
          StringComparison.Ordinal
        )
      )
        Interlocked.Increment(ref count);
      return ValueTask.FromResult(result);
    }
  }
}
