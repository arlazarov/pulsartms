using System.Data.Common;
using Application;
using Application.Caching;
using Application.Features.Routing.Background;
using Application.Features.Routing.Services.Routes;
using Application.Interfaces;
using Domain.Entities;
using Domain.Entities.Dispatch;
using Domain.Entities.Fleet;
using Domain.Models.Routing;
using Domain.Rules;
using Domain.Rules.Routing;
using Infrastructure;
using Infrastructure.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Load = Domain.Entities.Dispatch.Dispatch;

namespace Server.Tests.Routing;

// The background preparation of a truck's planning summary, as the server
// composes it, over SQLite and without providers: the truck's first load
// has a saved route whose stops are all passed, the second is current
// (the AMF1395 shape). Each case drives one preparation and counts the
// captures of the truck's inputs it made; the interleaving cases change
// something at the moment the preparation reads the current route.
[Trait("Category", "Routing")]
[Trait("Kind", "Integration")]
public sealed class PlanningSummaryRefreshTests
{
  [Fact]
  public async Task OnePreparationCapturesOnceAndNamesTheCurrentWork()
  {
    await using var f = await Fixture.CreateAsync();

    var cold = f.Summary();
    Assert.True(cold.IsRefreshing);
    var captures = await f.PrepareAsync();
    var warm = f.Summary();

    Assert.Equal(1, captures);
    Assert.False(warm.IsRefreshing);
    Assert.NotNull(warm.State?.Plan);
    var inputs = f.Inputs();
    Assert.Equal(f.Next.Id, inputs.CurrentWork?.DispatchId);
    foreach (var result in new[] { cold, warm })
    {
      Assert.Equal(inputs.CurrentWork!.DispatchId, result.DispatchId);
      Assert.Equal(inputs.CurrentWork.ExecutionLegId, result.ExecutionLegId);
      Assert.Equal(
        inputs.CurrentAssignmentRevision ?? 0,
        result.AssignmentRevision
      );
    }
  }

  // This process' cached inputs are out of date: tracking passed the first
  // load elsewhere. The preparation builds from its own fresh capture, not
  // from the cached copy, so it captures once and needs no second look. Its
  // result is for other work than the readers here still ask about, so it
  // is not published under their signature (stage 4a): until the inputs
  // are invalidated, this process' readers agree on their own current work,
  // marked updating; after, they all move to the next.
  [Fact]
  public async Task APreparationBuildsFromItsOwnCaptureNotACachedOne()
  {
    await using var f = await Fixture.CreateAsync(firstPassed: false);
    Assert.Equal(f.Passed.Id, f.Summary().DispatchId);
    await f.PassFirstAsync();

    var captures = await f.PrepareAsync();
    var behind = f.Summary();
    f.Root.GetRequiredService<ReadCache>()
      .InvalidateItem("planning-inputs", f.Truck.Id);
    var caughtUp = f.Summary();

    Assert.Equal(1, captures);
    Assert.Equal((f.Passed.Id, true), (behind.DispatchId, behind.IsRefreshing));
    Assert.Equal(f.Next.Id, caughtUp.DispatchId);
  }

  // The map shows the passed first load beside the current one: the
  // summary carries its conflict, cold and prepared, from the same inputs
  // that name the current work (stage 3b).
  [Fact]
  public async Task TheSummaryCarriesThePassedWorksConflict()
  {
    await using var f = await Fixture.CreateAsync();
    var expected = new WorkConflictNotice(
      f.Passed.Id,
      null,
      1395,
      "route_passed_not_delivered"
    );

    var cold = f.Summary();
    await f.PrepareAsync();
    var warm = f.Summary();

    Assert.True(cold.IsRefreshing);
    Assert.Equal([expected], cold.WorkConflicts);
    Assert.False(warm.IsRefreshing);
    Assert.Equal([expected], warm.WorkConflicts);
    Assert.Equal(f.Next.Id, warm.DispatchId);
  }

  // Stage 4a: tracking passes the first load on another process. The
  // commit's summary notice never reaches this one; only the planning
  // inputs are invalidated here, as the cache relay does. The summary
  // prepared for the passed load is not served as current: readers get the
  // inputs' new current work at once, and it is prepared again.
  [Fact]
  public async Task ASummaryForWorkTrackingPassedIsRetiredWithoutACommitNotice()
  {
    await using var f = await Fixture.CreateAsync(firstPassed: false);
    f.Summary();
    await f.PrepareAsync();
    var prepared = f.Summary();
    Assert.Equal(f.Passed.Id, prepared.DispatchId);
    Assert.False(prepared.IsRefreshing);

    await f.PassFirstAsync();
    f.Root.GetRequiredService<ReadCache>()
      .InvalidateItem("planning-inputs", f.Truck.Id);
    var after = f.Summary();

    Assert.Equal(f.Next.Id, after.DispatchId);
    Assert.True(after.IsRefreshing);
    Assert.Equal(f.Signature(), f.Cache.Take()?.Signature);
  }

  // Planning refuses the current work. The refusal speaks for it - not for
  // the passed first load - and is checked with one more capture before it
  // is kept.
  [Fact]
  public async Task ARefusalSpeaksForTheCurrentWork()
  {
    await using var f = await Fixture.CreateAsync(reviewNext: true);
    Assert.Equal(f.Next.Id, f.Summary().DispatchId);

    var captures = await f.PrepareAsync();
    var refused = f.Summary();

    Assert.Equal(2, captures);
    Assert.Contains("Review", refused.Message);
    Assert.Null(refused.State);
    Assert.Equal(f.Next.Id, refused.DispatchId);
    Assert.Null(refused.ExecutionLegId);
  }

  // Settings change while the summary is built. It was built under the old
  // settings and is not stored under their signature, and it is not built
  // again and again while nobody asks for the new settings; the first
  // reader to ask makes it due at once.
  [Fact]
  public async Task ASettingsChangeWhileBuildingIsNotPublished()
  {
    await using var f = await Fixture.CreateAsync();
    var signature = f.Signature();
    f.Summary();
    f.Probe.OnRouteRead = () =>
      f.Root.GetRequiredService<ReadCache>().Invalidate(ReadGroups.Settings);

    await f.PrepareAsync();

    Assert.Null(f.Cache.Read(f.Key, signature));
    Assert.Null(f.Cache.Take());
    Assert.NotEqual(signature, f.Signature());
    Assert.True(f.Summary().IsRefreshing);
    Assert.Equal(f.Signature(), f.Cache.Take()?.Signature);
  }

  // A commit for the truck while the summary is built: the late result is
  // not published, the entry is not prepared a second time alongside, and
  // one preparation follows.
  [Fact]
  public async Task ACommitWhileBuildingIsFollowedByOnePreparation()
  {
    await using var f = await Fixture.CreateAsync();
    var signature = f.Signature();
    f.Summary();
    PlanningSummaryCache.Work? during = null;
    f.Probe.OnRouteRead = () =>
    {
      f.Cache.Committed(Company.Amf, f.Truck.Id);
      during = f.Cache.Take();
    };

    await f.PrepareAsync();

    Assert.Null(during);
    Assert.Null(f.Cache.Read(f.Key, signature));
    Assert.NotNull(f.Cache.Take());
    Assert.Null(f.Cache.Take());
  }

  private sealed class Fixture(
    SqliteConnection connection,
    ServiceProvider root,
    Probe probe
  ) : IAsyncDisposable
  {
    public ServiceProvider Root => root;
    public Probe Probe => probe;
    public PlanningSummaryCache Cache =>
      root.GetRequiredService<PlanningSummaryCache>();
    public Truck Truck { get; } =
      new()
      {
        Id = Guid.NewGuid(),
        ExternalId = "v-11006",
        UnitNumber = "11006",
        IsActive = true,
      };
    public Load Passed { get; private set; } = null!;
    public Load Next { get; private set; } = null!;
    public PlanningSummaryCache.Key Key => new(Company.Amf, Truck.Id);

    public static async Task<Fixture> CreateAsync(
      bool reviewNext = false,
      bool firstPassed = true
    )
    {
      var connection = new SqliteConnection("Data Source=:memory:");
      await connection.OpenAsync();
      var probe = new Probe();
      var services = new ServiceCollection();
      var configuration = new ConfigurationBuilder().Build();
      services.AddLogging();
      services.AddSingleton<IConfiguration>(configuration);
      services.AddApplication();
      services.AddInfrastructure(configuration);
      services.RemoveAll<DbContextOptions<AppDbContext>>();
      services.RemoveAll<IDbContextOptionsConfiguration<AppDbContext>>();
      services.AddDbContext<AppDbContext>(o =>
        o.UseSqlite(connection).AddInterceptors(probe)
      );
      services.RemoveAll<ICurrentCompany>();
      services.AddSingleton<ICurrentCompany>(new TestCompany());
      var root = services.BuildServiceProvider();
      var fixture = new Fixture(connection, root, probe);
      await using var seed = root.CreateAsyncScope();
      var db = seed.ServiceProvider.GetRequiredService<AppDbContext>();
      await db.Database.EnsureCreatedAsync();
      db.Trucks.Add(fixture.Truck);
      fixture.Passed = fixture.Add(db, 1395, "in_transit", 0);
      fixture.Next = fixture.Add(db, 1412, "assigned", 1);
      if (reviewNext)
        db.DispatchSourceLinks.Add(
          new DispatchSourceLink
          {
            Provider = "source",
            ExternalId = "1412",
            DispatchId = fixture.Next.Id,
            Dispatch = fixture.Next,
            ExecutionReviewReason = "Review the initial assignment.",
          }
        );
      await db.SaveChangesAsync();
      await SavePlanAsync(
        db,
        fixture.Truck,
        fixture.Passed,
        passed: firstPassed
      );
      await SavePlanAsync(db, fixture.Truck, fixture.Next, passed: false);
      return fixture;
    }

    private Load Add(AppDbContext db, int number, string status, int day)
    {
      var date = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(day);
      var id = Guid.NewGuid();
      var load = new Load
      {
        Id = id,
        LoadNumber = number,
        Status = status,
        TruckId = Truck.Id,
        ShipDate = date,
        DeliveryDate = date,
        Stops =
        [
          new DispatchStop
          {
            Id = Guid.NewGuid(),
            DispatchId = id,
            Sequence = 1,
            Job = "Pick Up",
            Address = $"{number} pickup",
            TruckId = Truck.Id,
            ScheduledDate = date,
            Latitude = 42.9m,
            Longitude = -77.9m,
          },
          new DispatchStop
          {
            Id = Guid.NewGuid(),
            DispatchId = id,
            Sequence = 2,
            Job = "Drop Off",
            Address = $"{number} delivery",
            TruckId = Truck.Id,
            ScheduledDate = date,
            Latitude = 39.9m,
            Longitude = -76.7m,
          },
        ],
      };
      db.Dispatches.Add(load);
      return load;
    }

    private static async Task SavePlanAsync(
      AppDbContext db,
      Truck truck,
      Load load,
      bool passed
    )
    {
      var persisted = await db
        .Dispatches.AsNoTracking()
        .Include(x => x.Stops)
        .SingleAsync(x => x.Id == load.Id);
      var plan = new RoutePlan
      {
        Id = Guid.NewGuid(),
        DispatchId = load.Id,
        TruckId = truck.Id,
        Version = 1,
        CalculatedAt = DateTime.UtcNow,
        Tracking = new() { AllStopsPassed = passed },
        Stops = persisted
          .Stops.OrderBy(x => x.Sequence)
          .Select(stop => new PlanStop(
            stop.Id,
            stop.Name,
            stop.Address,
            stop.Sequence,
            new((double)stop.Latitude!.Value, (double)stop.Longitude!.Value)
          ))
          .ToList(),
        Route = new()
        {
          Miles = 10,
          Legs = [new(10, 600, [new(42.9, -77.9), new(39.9, -76.7)])],
        },
      };
      db.DispatchRoutePlans.Add(
        new()
        {
          Id = plan.Id,
          DispatchId = load.Id,
          TruckId = truck.Id,
          InputHash = RoutePlanInputs.Hash(persisted, plan.Profile),
          PlanJson = RoutePlanStorage.Serialize(plan),
        }
      );
      await db.SaveChangesAsync();
    }

    // The first load's stops passed, written as another process would: the
    // row changes, this process' cached inputs do not.
    public async Task PassFirstAsync()
    {
      await using var scope = root.CreateAsyncScope();
      var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
      var entry = await db.DispatchRoutePlans.SingleAsync(x =>
        x.DispatchId == Passed.Id
      );
      var plan = RoutePlanStorage.Read(entry)!;
      plan.Tracking.AllStopsPassed = true;
      entry.PlanJson = RoutePlanStorage.Serialize(plan);
      await db.SaveChangesAsync();
    }

    public AutomaticPlanningResult Summary()
    {
      using var scope = root.CreateScope();
      return scope
        .ServiceProvider.GetRequiredService<PlanningSummaryReader>()
        .ForTruckAsync(Truck.Id, default)
        .GetAwaiter()
        .GetResult();
    }

    public TruckPlanningInputs Inputs()
    {
      using var scope = root.CreateScope();
      return scope
        .ServiceProvider.GetRequiredService<TruckPlanningInputsReader>()
        .ReadFreshAsync(Truck.Id, default)
        .GetAwaiter()
        .GetResult()!;
    }

    public string Signature()
    {
      using var scope = root.CreateScope();
      return scope
        .ServiceProvider.GetRequiredService<PlanningSummaryReader>()
        .Signature(Inputs());
    }

    // One preparation of the entry a reader asked for; the captures of the
    // truck's inputs it made.
    public async Task<int> PrepareAsync()
    {
      var work = Cache.Take()!;
      var operation = (PlanningSummaryOperation)
        root.GetRequiredService<IPlanningSummaryOperation>();
      probe.Start();
      try
      {
        await operation.RefreshAsync(work, default);
        return probe.Captures;
      }
      finally
      {
        probe.Stop();
      }
    }

    public async ValueTask DisposeAsync()
    {
      await root.DisposeAsync();
      await connection.DisposeAsync();
    }
  }

  private sealed class Probe : DbCommandInterceptor
  {
    private bool enabled;

    public int Captures { get; private set; }

    // Run once, when the preparation first reads a saved route in full -
    // after it captured the inputs, before it publishes.
    public Action? OnRouteRead { get; set; }

    public void Start()
    {
      Captures = 0;
      enabled = true;
    }

    public void Stop() => enabled = false;

    public override ValueTask<
      InterceptionResult<DbDataReader>
    > ReaderExecutingAsync(
      DbCommand command,
      CommandEventData eventData,
      InterceptionResult<DbDataReader> result,
      CancellationToken cancellationToken = default
    )
    {
      if (enabled)
      {
        var text = command.CommandText;
        // Every capture reads the saved routes' metadata exactly once.
        if (text.Contains("'storedAssignmentRevision'"))
          Captures++;
        else if (
          text.Contains("\"PlanJson\"")
          && text.Contains("FROM \"DispatchRoutePlans\"")
          && OnRouteRead is { } hook
        )
        {
          OnRouteRead = null;
          hook();
        }
      }
      return ValueTask.FromResult(result);
    }
  }
}
