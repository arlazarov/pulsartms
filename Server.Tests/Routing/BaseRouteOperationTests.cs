using Application.Caching;
using Application.Features.Routing.Background;
using Application.Features.Routing.Interfaces;
using Application.Features.Routing.Models;
using Application.Features.Routing.Options;
using Application.Features.Routing.Services.Addresses;
using Application.Features.Routing.Services.Deadheads;
using Application.Features.Routing.Services.Routes;
using Application.Interfaces;
using Domain.Entities.Fleet;
using Infrastructure.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Server.Tests.Routing;

using Dispatch = global::Domain.Entities.Dispatch.Dispatch;

[Trait("Category", "Routing")]
[Trait("Kind", "Integration")]
public sealed class BaseRouteOperationTests
{
  [Fact]
  public async Task UnchangedScansDoNotPrepareAgainAndFinancialChangesReuseTheRoad()
  {
    await using var fixture = await Fixture.CreateAsync();
    var id = await fixture.AddAsync(0);
    await fixture.Operation.RunOnceAsync(default);
    Assert.Equal(1, fixture.Router.Calls);
    await fixture.Operation.RunOnceAsync(default);
    Assert.Equal(1, fixture.Router.Calls);
    Assert.Equal(0, fixture.Queue.PendingCount);
    await using (var scope = fixture.Root.CreateAsyncScope())
    {
      var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
      (await db.Dispatches.SingleAsync(x => x.Id == id)).Price = 200;
      await db.SaveChangesAsync();
    }
    await fixture.Operation.RunOnceAsync(default);
    Assert.Equal(1, fixture.Router.Calls);
    await using var check = fixture.Root.CreateAsyncScope();
    Assert.Equal(2, (await check.ServiceProvider.GetRequiredService<AppDbContext>().DispatchRates.SingleAsync()).LoadedRatePerMile);
  }

  [Fact]
  public async Task ExplicitDemandBypassesTheSpeculativeHorizonWithoutRepeatingItOnEveryPoll()
  {
    await using var fixture = await Fixture.CreateAsync();
    var id = await fixture.AddAsync(30);
    await fixture.AddAsync(30, "unassigned");
    await fixture.Operation.RunOnceAsync(default);
    Assert.Equal(0, fixture.Router.Calls);
    fixture.Queue.Request(id, "missing-road");
    await fixture.Operation.RunOnceAsync(default);
    Assert.Equal(1, fixture.Router.Calls);
    fixture.Queue.Request(id, "missing-road");
    await fixture.Operation.RunOnceAsync(default);
    Assert.Equal(1, fixture.Router.Calls);
    Assert.Equal(0, fixture.Queue.PendingCount);
  }

  [Fact]
  public async Task ProfileChangeDuringProviderWorkCannotBeAcknowledgedByTheOldAttempt()
  {
    await using var fixture = await Fixture.CreateAsync();
    await fixture.AddAsync(0);
    fixture.Router.BeforeCalculate = async (_, ct) =>
    {
      fixture.Router.BeforeCalculate = null;
      await using var scope = fixture.Root.CreateAsyncScope();
      var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
      var services = scope.ServiceProvider.GetRequiredService<PlanningTestServices>();
      await new TruckPlanningProfileService(db, fixture.Reads, services.Settings)
        .SaveAsync(fixture.Truck.Id, new() { UsesFleetDefaults = true, HeightFeet = 14 }, ct);
    };
    await fixture.Operation.RunOnceAsync(default);
    Assert.Equal(new[] { 13.5 }, fixture.Router.Heights);
    await fixture.Operation.RunOnceAsync(default);
    Assert.Equal(new[] { 13.5, 14 }, fixture.Router.Heights);
    Assert.Equal(0, fixture.Queue.PendingCount);
  }

  [Fact]
  public async Task InitialWorkerScanStartsImmediatelyAndCancellationDoesNotStrandTheUntakenBatch()
  {
    await using var fixture = await Fixture.CreateAsync();
    await fixture.AddAsync(0);
    var second = await fixture.AddAsync(1);
    var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    fixture.Router.BeforeCalculate = async (_, ct) =>
    {
      started.TrySetResult();
      await Task.Delay(Timeout.InfiniteTimeSpan, ct);
    };
    using var cancellation = new CancellationTokenSource();
    var running = fixture.Operation.RunAsync(cancellation.Token);
    await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
    await cancellation.CancelAsync();
    await Assert.ThrowsAnyAsync<OperationCanceledException>(() => running);
    Assert.Equal(2, fixture.Queue.PendingCount);
    Assert.Equal(second, Assert.Single(fixture.Queue.Take(10)).DispatchId);
  }

  [Fact]
  public async Task RepairPagesReachLaterLoadsAndStopOnlyAssignmentsStayDeduplicated()
  {
    await using var fixture = await Fixture.CreateAsync(new() { ScanPageSize = 2, BatchSize = 10 });
    await fixture.AddAsync(0, stopOnly: true);
    await fixture.AddAsync(1);
    await fixture.AddAsync(2);
    await fixture.Operation.RunOnceAsync(default);
    await fixture.Operation.RunOnceAsync(default);
    var calls = fixture.Router.Calls;
    Assert.True(calls >= 3);
    await fixture.Operation.RunOnceAsync(default);
    await fixture.Operation.RunOnceAsync(default);
    Assert.Equal(calls, fixture.Router.Calls);
    Assert.Equal(0, fixture.Queue.PendingCount);
    await using var scope = fixture.Root.CreateAsyncScope();
    Assert.Equal(3, await scope.ServiceProvider.GetRequiredService<AppDbContext>().DispatchBaseRoutes.CountAsync());
  }

  [Fact]
  public async Task GeometryRepairWithRetainedMileageRemainsPendingUntilItsPersistedRetryDeadline()
  {
    await using var fixture = await Fixture.CreateAsync();
    await fixture.AddAsync(0);
    var id = await fixture.AddAsync(1);
    await fixture.Operation.RunOnceAsync(default);
    var calls = fixture.Router.Calls;
    var retryAfter = DateTime.UtcNow.AddMinutes(20);
    await using (var scope = fixture.Root.CreateAsyncScope())
    {
      var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
      var saved = await db.DispatchDeadheads.SingleAsync(x => x.DispatchId == id);
      Assert.Equal(100m, saved.Miles);
      saved.RouteJson = null;
      saved.RetryAfter = retryAfter;
      await db.SaveChangesAsync();
    }
    fixture.Queue.MarkDirty(id);
    await fixture.Operation.RunOnceAsync(default);
    Assert.Equal(calls, fixture.Router.Calls);
    Assert.Equal(1, fixture.Queue.PendingCount);
    Assert.Empty(fixture.Queue.Take(10));
    await fixture.Operation.RunOnceAsync(default);
    Assert.Equal(calls, fixture.Router.Calls);
    Assert.Equal(1, fixture.Queue.PendingCount);
    await using var check = fixture.Root.CreateAsyncScope();
    var pending = await check.ServiceProvider.GetRequiredService<AppDbContext>().DispatchDeadheads.AsNoTracking().SingleAsync(x => x.DispatchId == id);
    Assert.Equal(100m, pending.Miles);
    Assert.Equal(retryAfter, pending.RetryAfter);
  }

  private sealed class Fixture(SqliteConnection connection, ServiceProvider root, ReadCache reads,
    RoutePreparationQueue queue, Router router, ManualTimeProvider clock, Truck truck, BaseRouteOperation operation) : IAsyncDisposable
  {
    private int loadNumber;
    public ServiceProvider Root => root;
    public ReadCache Reads => reads;
    public RoutePreparationQueue Queue => queue;
    public Router Router => router;
    public Truck Truck => truck;
    public BaseRouteOperation Operation => operation;

    public static async Task<Fixture> CreateAsync(RoutePreparationOptions? configuration = null)
    {
      var connection = new SqliteConnection("Data Source=:memory:");
      await connection.OpenAsync();
      var clock = new ManualTimeProvider(DateTimeOffset.UtcNow);
      var options = Options.Create(configuration ?? new RoutePreparationOptions());
      var queue = new RoutePreparationQueue(options, clock);
      var reads = TestCache.Create();
      var router = new Router();
      var services = new ServiceCollection();
      services.AddSingleton(reads);
      services.AddScoped(_ => new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connection).Options));
      services.AddScoped<IAppDbContext>(provider => provider.GetRequiredService<AppDbContext>());
      services.AddScoped(provider => new PlanningTestServices(provider.GetRequiredService<IAppDbContext>(), router, reads: reads));
      services.AddScoped(provider => provider.GetRequiredService<PlanningTestServices>().Routes);
      services.AddScoped(provider => provider.GetRequiredService<PlanningTestServices>().Deadheads);
      services.AddScoped(provider => new BaseRouteService(provider.GetRequiredService<IAppDbContext>(), router));
      services.AddScoped(provider => new StopAddressService(provider.GetRequiredService<IAppDbContext>(), new NoAddressLookup(), reads, queue));
      var root = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
      var truck = new Truck { Id = Guid.NewGuid(), ExternalId = "preparation", UnitNumber = "Preparation", IsActive = true };
      await using (var scope = root.CreateAsyncScope())
      {
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await db.Database.EnsureCreatedAsync();
        db.Trucks.Add(truck);
        await db.SaveChangesAsync();
      }
      var operation = new BaseRouteOperation(root.GetRequiredService<IServiceScopeFactory>(), NullLogger<BaseRouteOperation>.Instance, queue, options, clock);
      return new(connection, root, reads, queue, router, clock, truck, operation);
    }

    public async Task<Guid> AddAsync(int days, string status = "assigned", bool stopOnly = false)
    {
      await using var scope = root.CreateAsyncScope();
      var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
      var date = DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime).AddDays(days);
      var load = new Dispatch { Id = Guid.NewGuid(), LoadNumber = ++loadNumber, TruckId = stopOnly ? null : truck.Id, Status = status,
        ShipDate = date, DeliveryDate = date, Price = 100, Currency = "USD", LoadedMiles = 100, Stops = [
          new() { Id = Guid.NewGuid(), TruckId = truck.Id, Sequence = 1, Job = "Pick Up", ScheduledDate = date,
            Latitude = 40, Longitude = -80 + days / 10m },
          new() { Id = Guid.NewGuid(), TruckId = truck.Id, Sequence = 2, Job = "Drop Off", ScheduledDate = date,
            Latitude = 41, Longitude = -79 + days / 10m }] };
      db.Dispatches.Add(load);
      await db.SaveChangesAsync();
      return load.Id;
    }

    public async ValueTask DisposeAsync()
    {
      await root.DisposeAsync();
      await connection.DisposeAsync();
    }
  }

  private sealed class Router : IRoutingProvider
  {
    public bool IsConfigured => true;
    public int Calls { get; private set; }
    public List<double> Heights { get; } = [];
    public Func<TruckRouteProfile, CancellationToken, Task>? BeforeCalculate { get; set; }
    public async Task<TruckRoute> CalculateAsync(IReadOnlyList<RoutePoint> points, TruckRouteProfile profile, CancellationToken ct)
    {
      Calls++;
      Heights.Add(profile.HeightFeet);
      if (BeforeCalculate is { } before) await before(profile, ct);
      return new() { Miles = 100, Seconds = 100, Points = points.ToList(),
        Legs = points.Zip(points.Skip(1), (from, to) => new RouteLeg(100 / (points.Count - 1d), 100 / (points.Count - 1d), [from, to])).ToList() };
    }
    public Task<RoutePoint> GeocodeAsync(string address, CancellationToken ct) => throw new InvalidOperationException("No imported address lookup expected.");
  }

  private sealed class NoAddressLookup : IAddressGeocoder
  {
    public Task<RoutePoint> GeocodeAsync(string address, CancellationToken ct) => throw new InvalidOperationException("No address lookup expected.");
    public Task<ResolvedAddress> ResolveAsync(string address, CancellationToken ct) => throw new InvalidOperationException("No address lookup expected.");
  }
}
