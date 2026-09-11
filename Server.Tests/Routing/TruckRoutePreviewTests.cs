using Application.Features.Dispatch.Queries;
using Application.Features.Fleet.Interfaces;
using Application.Features.Fleet.Models;
using Application.Features.Fleet.Queries.GetFleetLocations;
using Application.Features.Routing.Exceptions;
using Application.Features.Routing.Interfaces;
using Application.Features.Routing.Models;
using Application.Features.Routing.Queries;
using Application.Features.Routing.Services.Routes;
using Application.Features.Synchronization.Options;
using Application.Models;
using Domain.Entities.Dispatch;
using Domain.Entities.Fleet;
using Infrastructure.Persistence;
using MediatR;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using System.Data.Common;
using System.Text.Json;

namespace Server.Tests.Routing;

using Dispatch = global::Domain.Entities.Dispatch.Dispatch;

[Trait("Category", "Routing")]
[Trait("Kind", "Integration")]
public sealed class TruckRoutePreviewTests
{
  [Theory]
  [InlineData("in_transit", false)]
  [InlineData("assigned", true)]
  public async Task OverdueCurrentLoadOwnsPreviewLiveReadAndEtaChainUntilDelivery(string status, bool pickedUp)
  {
    await using var fixture = await Fixture.CreateAsync();
    var current = await fixture.AddAsync(1);
    var next = await fixture.AddAsync(2);
    var yesterday = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(-1);
    current.Status = status;
    current.ShipDate = current.DeliveryDate = yesterday;
    current.Stops.ForEach(stop => stop.ScheduledDate = yesterday);
    if (pickedUp) current.Stops[0].PickedUpAt = DateTime.UtcNow.AddDays(-1);
    await fixture.Db.SaveChangesAsync();
    await fixture.SavePlanAsync(current);
    await fixture.SavePlanAsync(next);

    Assert.Equal(current.Id, (await fixture.Preview.ForTruckAsync(fixture.Truck.Id, default)).DispatchId);
    Assert.Equal(current.Id, Assert.Single(await fixture.Preview.GetAsync(default)).DispatchId);
    fixture.Hos.Allow = fixture.Sender.AllowTelemetry = true;
    var options = Options.Create(new SynchronizationOptions { Enabled = true });
    var reader = new PlanningReadService(fixture.Services.Routes, new(fixture.Memory, options), fixture.Sender,
      options, fixture.Services.Eta, fixture.Services.FuelPlans);
    Assert.Equal(current.Id, (await reader.ForTruckAsync(fixture.Truck.Id, default)).DispatchId);
    var chain = await fixture.Services.EtaInputs.DescribeAsync(fixture.Truck.Id, default);
    Assert.Equal(current.Id, chain!.RootDispatchId);
    Assert.Equal(new[] { current.Id, next.Id }, chain.Loads.Select(load => load.Id));

    current.Stops[^1].DeliveredAt = DateTime.UtcNow;
    await fixture.Db.SaveChangesAsync();
    fixture.Services.Reads.Invalidate("board");
    fixture.Services.Reads.Invalidate("dispatch");
    Assert.Equal(next.Id, (await fixture.Preview.ForTruckAsync(fixture.Truck.Id, default)).DispatchId);
    Assert.Equal(next.Id, (await reader.ForTruckAsync(fixture.Truck.Id, default)).DispatchId);
    Assert.Equal(next.Id, (await fixture.Services.EtaInputs.DescribeAsync(fixture.Truck.Id, default))!.RootDispatchId);
    Assert.Equal(0, fixture.Router.Calls);
  }

  [Fact]
  public async Task CurrentPollingOmitsOnlyKnownGeometryWhileBackgroundReadsKeepEveryCoordinate()
  {
    await using var fixture = await Fixture.CreateAsync();
    var load = await fixture.AddAsync(1);
    var entry = await fixture.SavePlanAsync(load);
    var stored = JsonSerializer.Deserialize<RoutePlan>(entry.PlanJson, RoutePlanningService.Json)!;
    stored.Route.Legs = [new(10, 600, Enumerable.Range(0, 1001).Select(i => new RoutePoint(40 + i / 1000d, -80)).ToList())];
    entry.PlanJson = RoutePlanStorage.Serialize(stored);
    await fixture.Db.SaveChangesAsync();
    fixture.Hos.Allow = fixture.Sender.AllowTelemetry = true;
    var options = Options.Create(new SynchronizationOptions { Enabled = true });
    var reader = new PlanningReadService(fixture.Services.Routes, new(fixture.Memory, options), fixture.Sender, options, fixture.Services.Eta, fixture.Services.FuelPlans);
    var initial = (await reader.ForDispatchAsync(load.Id, default)).State!.Plan!;
    Assert.False(initial.GeometryOmitted);
    Assert.Equal(2, initial.Route.Legs[0].Points.Count);
    load.Stops[0].Name = "Fresh pickup";
    await fixture.Db.SaveChangesAsync();
    fixture.Services.Reads.Invalidate("dispatch");
    fixture.Probe.Start();
    var current = (await reader.ForDispatchAsync(load.Id, default, stored.Id, stored.Version)).State!.Plan!;
    Assert.True(current.GeometryOmitted);
    Assert.Empty(current.Route.Legs[0].Points);
    Assert.Equal("Fresh pickup", current.Stops.Single(x => x.Id == load.Stops[0].Id).Name);
    Assert.False(current.InputsChanged);
    var outdated = (await reader.ForDispatchAsync(load.Id, default, stored.Id, stored.Version - 1)).State!.Plan!;
    Assert.False(outdated.GeometryOmitted);
    Assert.Equal(2, outdated.Route.Legs[0].Points.Count);
    var background = await fixture.Services.Routes.GetAsync(load.Id, default, cachedTelemetryOnly: true);
    Assert.False(background.Plan!.GeometryOmitted);
    Assert.Equal(1001, background.Plan.Route.Legs[0].Points.Count);
    Assert.Equal(0, fixture.Router.Calls);
  }

  [Fact]
  public async Task KnownCompletedPlanCannotOmitTheNextCurrentDispatchGeometry()
  {
    await using var fixture = await Fixture.CreateAsync();
    var first = await fixture.AddAsync(1);
    var next = await fixture.AddAsync(2);
    var completed = await fixture.SavePlanAsync(first, completed: true);
    var current = await fixture.SavePlanAsync(next);
    fixture.Hos.Allow = fixture.Sender.AllowTelemetry = true;
    var options = Options.Create(new SynchronizationOptions { Enabled = true });
    var reader = new PlanningReadService(fixture.Services.Routes, new(fixture.Memory, options), fixture.Sender, options, fixture.Services.Eta, fixture.Services.FuelPlans);
    var changed = await reader.ForTruckAsync(fixture.Truck.Id, default, completed.Id, 1);
    Assert.Equal(next.Id, changed.DispatchId);
    Assert.False(changed.State!.Plan!.GeometryOmitted);
    Assert.NotEmpty(changed.State.Plan.Route.Legs[0].Points);
    var known = await reader.ForTruckAsync(fixture.Truck.Id, default, current.Id, 1);
    Assert.Equal(next.Id, known.DispatchId);
    Assert.True(known.State!.Plan!.GeometryOmitted);
    Assert.Empty(known.State.Plan.Route.Legs[0].Points);
  }

  [Theory]
  [InlineData(false, false)]
  [InlineData(false, true)]
  [InlineData(true, false)]
  public async Task MetadataDisplayReadsPreserveRecommendationFilteringWithChangedInputsAndExactGps(bool stale, bool offRoute)
  {
    await using var fixture = await Fixture.CreateAsync();
    var load = await fixture.AddAsync(1);
    var entry = await fixture.SavePlanAsync(load);
    var stored = JsonSerializer.Deserialize<RoutePlan>(entry.PlanJson, RoutePlanningService.Json)!;
    var profile = await fixture.Services.Routes.ProfileAsync(fixture.Truck.Id, default);
    stored.FuelRecommendations = new() { SettingsSignature = PlanningSettingsService.Signature(profile),
      Stations = [new() { Name = "Behind", RouteMile = 2 }, new() { Name = "Ahead", RouteMile = 8 }] };
    entry.PlanJson = RoutePlanStorage.Serialize(stored);
    load.Stops[0].Address = "Changed pickup address";
    await fixture.Db.SaveChangesAsync();
    fixture.Sender.AllowTelemetry = true;
    fixture.Sender.Telemetry = new() { Trucks = [new() { TruckId = fixture.Truck.Id, Latitude = 40.5m,
      Longitude = offRoute ? -79.97m : -80m, UpdatedAt = stale ? DateTime.UtcNow.AddHours(-1) : DateTime.UtcNow }] };
    var resolved = await fixture.Services.Routes.LoadAsync(load.Id, default);
    var full = await fixture.Services.Routes.GetAsync(resolved, default, cachedTelemetryOnly: true);
    AutomaticPlanningService.ProjectRecommendations(full);
    var metadata = await fixture.Services.Routes.GetAsync(resolved, default, cachedTelemetryOnly: true,
      displayOnly: true, knownPlanId: stored.Id, knownVersion: stored.Version);
    Assert.True(metadata.Plan!.InputsChanged);
    Assert.True(metadata.Plan.GeometryOmitted);
    Assert.Empty(metadata.Plan.Route.Legs[0].Points);
    Assert.Equal(full.Progress, metadata.Progress);
    Assert.Equal(full.Plan!.FuelRecommendations!.Stations.Select(x => x.Name), metadata.Plan.FuelRecommendations!.Stations.Select(x => x.Name));
    Assert.Equal(stale ? 2 : 1, metadata.Plan.FuelRecommendations.Stations.Count);
  }

  [Fact]
  public async Task ColdPreviewReadsOnlySavedDataAndWarmPreviewReusesDispatchAndGeometryCaches()
  {
    await using var fixture = await Fixture.CreateAsync();
    var load = await fixture.AddAsync(1);
    var stored = await fixture.SavePlanAsync(load, fuel: true);
    fixture.Probe.Start();
    var result = await fixture.Preview.ForTruckAsync(fixture.Truck.Id, default);
    Assert.Equal(load.Id, result.DispatchId);
    var state = Assert.IsType<RoutePlanningState>(result.State);
    Assert.NotEmpty(state.Plan!.Route.Legs[0].Points);
    Assert.Empty(state.Plan.Route.Points);
    Assert.False(state.Plan.GeometryOmitted);
    Assert.Null(state.Plan.FuelPlan);
    Assert.Null(state.Plan.FuelRecommendations);
    Assert.Null(state.Progress);
    Assert.Null(state.FuelPercent);
    Assert.Null(state.Eta);
    Assert.Null(result.Hos);
    Assert.Equal(7, fixture.Probe.Reads);
    fixture.Probe.Start();
    state.Plan.Route.Legs.Clear();
    var repeated = await fixture.Preview.ForTruckAsync(fixture.Truck.Id, default);
    Assert.NotEmpty(repeated.State!.Plan!.Route.Legs);
    Assert.Equal(1, fixture.Probe.Reads);
    Assert.Equal(0, fixture.Router.Calls);
    Assert.Equal(0, fixture.Hos.Calls);
    Assert.Equal(0, fixture.Sender.TelemetryCalls);
    Assert.All(fixture.Sender.Requests, request =>
    {
      Assert.Equal(fixture.Truck.Id, request.TruckId);
      Assert.False(request.IncludeHos);
      Assert.False(request.IncludeFinancials);
    });
    var fleet = Assert.Single(await fixture.Preview.GetAsync(default));
    Assert.Null(fleet.State!.Plan!.FuelPlan);
    Assert.Null(fleet.State.Plan.FuelRecommendations);
    Assert.Equal(stored.PlanJson, (await fixture.Db.DispatchRoutePlans.AsNoTracking().SingleAsync()).PlanJson);
    Assert.False(fixture.Db.ChangeTracker.HasChanges());
  }

  [Fact]
  public async Task FirstUnsavedLoadKeepsItsIdentityInsteadOfSelectingALaterSavedRoute()
  {
    await using var fixture = await Fixture.CreateAsync();
    var first = await fixture.AddAsync(1);
    var later = await fixture.AddAsync(2);
    await fixture.SavePlanAsync(later);
    fixture.Probe.Start();
    var result = await fixture.Preview.ForTruckAsync(fixture.Truck.Id, default);
    Assert.Equal(first.Id, result.DispatchId);
    Assert.Null(result.State);
    Assert.Empty(await fixture.Preview.GetAsync(default));
  }

  [Fact]
  public async Task CompletedSavedLoadIsSkippedAndIdentityMatchesNormalPlanningRead()
  {
    await using var fixture = await Fixture.CreateAsync();
    var first = await fixture.AddAsync(1);
    var next = await fixture.AddAsync(2);
    await fixture.SavePlanAsync(first, completed: true);
    await fixture.SavePlanAsync(next);
    fixture.Probe.Start();
    var preview = await fixture.Preview.ForTruckAsync(fixture.Truck.Id, default);
    Assert.Equal(next.Id, preview.DispatchId);
    Assert.Equal(0, fixture.Hos.Calls);
    Assert.Equal(0, fixture.Sender.TelemetryCalls);
    fixture.Hos.Allow = fixture.Sender.AllowTelemetry = true;
    var options = Options.Create(new SynchronizationOptions { Enabled = true });
    var normal = new PlanningReadService(fixture.Services.Routes, new(fixture.Memory, options), fixture.Sender, options, fixture.Services.Eta, fixture.Services.FuelPlans);
    var live = await normal.ForTruckAsync(fixture.Truck.Id, default);
    Assert.Equal(preview.DispatchId, live.DispatchId);
    Assert.Equal(0, fixture.Router.Calls);
  }

  [Fact]
  public async Task ChangedStopsDoNotLetAnOldCompletedPlanSkipTheCurrentDispatch()
  {
    await using var fixture = await Fixture.CreateAsync();
    var first = await fixture.AddAsync(1);
    var next = await fixture.AddAsync(2);
    await fixture.SavePlanAsync(first, completed: true);
    await fixture.SavePlanAsync(next);
    first.Stops[0].Address = "Changed pickup street";
    await fixture.Db.SaveChangesAsync();
    fixture.Probe.Start();
    var result = await fixture.Preview.ForTruckAsync(fixture.Truck.Id, default);
    Assert.Equal(first.Id, result.DispatchId);
    Assert.Null(result.State);
    Assert.Empty(await fixture.Preview.GetAsync(default));
    fixture.Hos.Allow = fixture.Sender.AllowTelemetry = true;
    var options = Options.Create(new SynchronizationOptions { Enabled = true });
    var reader = new PlanningReadService(fixture.Services.Routes, new(fixture.Memory, options), fixture.Sender, options, fixture.Services.Eta, fixture.Services.FuelPlans);
    var live = await reader.ForTruckAsync(fixture.Truck.Id, default);
    Assert.Equal(first.Id, live.DispatchId);
    Assert.True(live.State!.Plan!.InputsChanged);
  }

  [Fact]
  public async Task ChangedTruckDimensionsInvalidateFleetPreviewAndRejectOldSavedGeometry()
  {
    await using var fixture = await Fixture.CreateAsync();
    var load = await fixture.AddAsync(1);
    await fixture.SavePlanAsync(load);
    var fleet = Assert.Single(await fixture.Preview.GetAsync(default));
    var profile = fleet.State!.Profile;
    profile.HeightFeet = 14;
    await new TruckPlanningProfileService(fixture.Db, fixture.Services.Reads, fixture.Services.Settings)
      .SaveAsync(fixture.Truck.Id, profile, default);
    fixture.Probe.Start();
    var result = await fixture.Preview.ForTruckAsync(fixture.Truck.Id, default);
    Assert.Equal(load.Id, result.DispatchId);
    Assert.Null(result.State);
    Assert.Empty(await fixture.Preview.GetAsync(default));
    Assert.Equal(0, fixture.Router.Calls);
    Assert.Equal(0, fixture.Hos.Calls);
  }

  [Fact]
  public async Task CommittedTrackingChangesInvalidateTheSavedDisplayBeforeItsTtlExpires()
  {
    await using var fixture = await Fixture.CreateAsync();
    var first = await fixture.AddAsync(1);
    var next = await fixture.AddAsync(2);
    var entry = await fixture.SavePlanAsync(first);
    await fixture.SavePlanAsync(next);
    var preview = await fixture.Preview.ForTruckAsync(fixture.Truck.Id, default);
    Assert.Equal(first.Id, preview.DispatchId);
    var plan = preview.State!.Plan!;
    plan.Tracking.AllStopsPassed = true;
    var profiles = new TruckPlanningProfileService(fixture.Db, fixture.Services.Reads, fixture.Services.Settings);
    await new RoutePlanStore(fixture.Db, fixture.Services.Reads, profiles).SaveAsync(entry, plan, default);
    fixture.Probe.Start();
    Assert.Equal(next.Id, (await fixture.Preview.ForTruckAsync(fixture.Truck.Id, default)).DispatchId);
  }

  [Fact]
  public async Task DispatchInvalidationChangesCurrentIdentityWithoutWaitingForBoardTtl()
  {
    await using var fixture = await Fixture.CreateAsync();
    var first = await fixture.AddAsync(1);
    var next = await fixture.AddAsync(2);
    await fixture.SavePlanAsync(first);
    await fixture.SavePlanAsync(next);
    Assert.Equal(first.Id, (await fixture.Preview.ForTruckAsync(fixture.Truck.Id, default)).DispatchId);
    first.Status = "completed";
    await fixture.Db.SaveChangesAsync();
    fixture.Services.Reads.Invalidate("dispatch");
    fixture.Services.Reads.Invalidate("board");
    fixture.Probe.Start();
    Assert.Equal(next.Id, (await fixture.Preview.ForTruckAsync(fixture.Truck.Id, default)).DispatchId);
  }

  [Fact]
  public async Task SplitAssignmentsRejectSavedGeometryEvenWhenItsHeaderMatches()
  {
    await using var fixture = await Fixture.CreateAsync();
    var load = await fixture.AddAsync(1);
    await fixture.SavePlanAsync(load);
    var other = new Truck { Id = Guid.NewGuid(), ExternalId = "other", UnitNumber = "Other", IsActive = true };
    fixture.Db.Trucks.Add(other);
    load.Stops[0].TruckId = other.Id;
    await fixture.Db.SaveChangesAsync();
    fixture.Probe.Start();
    await Assert.ThrowsAsync<RoutePlanningException>(() => fixture.Preview.ForTruckAsync(fixture.Truck.Id, default));
    Assert.Empty(await fixture.Preview.GetAsync(default));
  }

  [Fact]
  public async Task AUniqueStopOnlyAssignmentUsesTheSameServerResolutionAsPlanning()
  {
    await using var fixture = await Fixture.CreateAsync();
    var load = await fixture.AddAsync(1);
    await fixture.SavePlanAsync(load);
    load.TruckId = null;
    await fixture.Db.SaveChangesAsync();
    fixture.Probe.Start();
    var result = await fixture.Preview.ForTruckAsync(fixture.Truck.Id, default);
    Assert.Equal(load.Id, result.DispatchId);
    Assert.NotNull(result.State?.Plan);
  }

  [Theory]
  [InlineData(false)]
  [InlineData(true)]
  public async Task AStoredPlanCannotSupplyGeometryForAnotherTruck(bool changeSerializedOwner)
  {
    await using var fixture = await Fixture.CreateAsync();
    var load = await fixture.AddAsync(1);
    var stored = await fixture.SavePlanAsync(load);
    var other = new Truck { Id = Guid.NewGuid(), ExternalId = "other", UnitNumber = "Other", IsActive = true };
    fixture.Db.Trucks.Add(other);
    if (changeSerializedOwner)
    {
      var plan = System.Text.Json.JsonSerializer.Deserialize<RoutePlan>(stored.PlanJson, RoutePlanningService.Json)!;
      plan.TruckId = other.Id;
      stored.PlanJson = RoutePlanStorage.Serialize(plan);
    }
    else stored.TruckId = other.Id;
    await fixture.Db.SaveChangesAsync();
    fixture.Probe.Start();
    var result = await fixture.Preview.ForTruckAsync(fixture.Truck.Id, default);
    Assert.Equal(load.Id, result.DispatchId);
    Assert.Null(result.State);
  }

  [Fact]
  public async Task NoRemainingLoadHasNoCurrentIdentityAndCancellationDoesNoWork()
  {
    await using var fixture = await Fixture.CreateAsync();
    var load = await fixture.AddAsync(1);
    await fixture.SavePlanAsync(load, completed: true);
    fixture.Probe.Start();
    var result = await fixture.Preview.ForTruckAsync(fixture.Truck.Id, default);
    Assert.Null(result.DispatchId);
    Assert.Null(result.State);
    fixture.Probe.Start();
    using var cancellation = new CancellationTokenSource();
    await cancellation.CancelAsync();
    await Assert.ThrowsAnyAsync<OperationCanceledException>(() => fixture.Preview.ForTruckAsync(fixture.Truck.Id, cancellation.Token));
    Assert.Equal(0, fixture.Probe.Reads);
    Assert.False((await new GetTruckRoutePreviewValidator().ValidateAsync(new GetTruckRoutePreviewQuery(Guid.Empty))).IsValid);
  }

  private sealed class Fixture(SqliteConnection connection, AppDbContext db, ReadOnlyProbe probe) : IAsyncDisposable
  {
    public AppDbContext Db => db;
    public ReadOnlyProbe Probe => probe;
    public Truck Truck { get; } = new() { Id = Guid.NewGuid(), ExternalId = "preview", UnitNumber = "Preview", IsActive = true };
    public RejectingRouter Router { get; } = new();
    public RejectingHos Hos { get; } = new();
    public BoardSender Sender { get; } = new();
    public MemoryCache Memory { get; } = new(new MemoryCacheOptions());
    public PlanningTestServices Services { get; private set; } = null!;
    public RoutePreviewService Preview { get; private set; } = null!;

    public static async Task<Fixture> CreateAsync()
    {
      var connection = new SqliteConnection("Data Source=:memory:");
      await connection.OpenAsync();
      var probe = new ReadOnlyProbe();
      var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connection).AddInterceptors(probe).Options);
      await db.Database.EnsureCreatedAsync();
      var fixture = new Fixture(connection, db, probe);
      fixture.Services = new(db, fixture.Router, fixture.Sender);
      fixture.Sender.Board = new(db, fixture.Services.Reads, fixture.Hos, fixture.Services.Deadheads, fixture.Services.Forecasts);
      fixture.Preview = new(db, fixture.Sender, fixture.Services.Reads, fixture.Services.Displays, fixture.Services.Routes, fixture.Memory);
      db.Trucks.Add(fixture.Truck);
      await db.SaveChangesAsync();
      return fixture;
    }

    public async Task<Dispatch> AddAsync(int order)
    {
      var date = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(order);
      var load = new Dispatch { Id = Guid.NewGuid(), LoadNumber = order, Status = order == 1 ? "in_transit" : "assigned",
        TruckId = Truck.Id, ShipDate = date, DeliveryDate = date, Stops = [
          new() { Id = Guid.NewGuid(), TruckId = Truck.Id, Sequence = 1, Job = "Pick Up", Address = "Pickup", ScheduledDate = date, Latitude = 40, Longitude = -80 },
          new() { Id = Guid.NewGuid(), TruckId = Truck.Id, Sequence = 2, Job = "Drop Off", Address = "Delivery", ScheduledDate = date, Latitude = 41, Longitude = -80 }] };
      Db.Dispatches.Add(load);
      await Db.SaveChangesAsync();
      return load;
    }

    public async Task<DispatchRoutePlan> SavePlanAsync(Dispatch load, bool completed = false, bool fuel = false)
    {
      var persisted = await Db.Dispatches.AsNoTracking().Include(x => x.Stops).SingleAsync(x => x.Id == load.Id);
      var plan = new RoutePlan { Id = Guid.NewGuid(), DispatchId = load.Id, TruckId = Truck.Id, Version = 1,
        CalculatedAt = DateTime.UtcNow, Tracking = new() { AllStopsPassed = completed },
        Stops = load.Stops.Select(stop => new PlanStop(stop.Id, stop.Name, stop.Address, stop.Sequence,
          new((double)stop.Latitude!.Value, (double)stop.Longitude!.Value))).ToList(),
        Route = new() { Miles = 10, Legs = [new(10, 600, [new(40, -80), new(41, -80)])] },
        FuelPlan = fuel ? new() { PurchaseGallons = 100 } : null, FuelRecommendations = fuel ? new() : null };
      var entry = new DispatchRoutePlan { Id = plan.Id, DispatchId = load.Id, TruckId = Truck.Id,
        InputHash = RoutePlanningService.HashInputs(persisted, plan.Profile), PlanJson = RoutePlanStorage.Serialize(plan) };
      Db.DispatchRoutePlans.Add(entry);
      await Db.SaveChangesAsync();
      return entry;
    }

    public async ValueTask DisposeAsync()
    {
      Services.Dispose();
      Memory.Dispose();
      await Db.DisposeAsync();
      await connection.DisposeAsync();
    }
  }

  private sealed class BoardSender : ISender
  {
    public GetDispatchBoardHandler Board { get; set; } = null!;
    public List<GetDispatchBoardQuery> Requests { get; } = [];
    public bool AllowTelemetry { get; set; }
    public int TelemetryCalls { get; private set; }
    public FleetLocationsResponse Telemetry { get; set; } = new();
    public async Task<TResponse> Send<TResponse>(IRequest<TResponse> request, CancellationToken ct = default)
    {
      if (request is GetDispatchBoardQuery board)
      {
        Requests.Add(board);
        return (TResponse)(object)await Board.Handle(board, ct);
      }
      if (AllowTelemetry && request is GetFleetLocationsQuery { CachedOnly: true })
      {
        TelemetryCalls++;
        return (TResponse)(object)RequestResponse<FleetLocationsResponse>.Ok(Telemetry);
      }
      throw new InvalidOperationException("The preview must not request live planning data.");
    }
    public Task Send<TRequest>(TRequest request, CancellationToken ct = default) where TRequest : IRequest => throw new NotSupportedException();
    public Task<object?> Send(object request, CancellationToken ct = default) => throw new NotSupportedException();
    public IAsyncEnumerable<TResponse> CreateStream<TResponse>(IStreamRequest<TResponse> request, CancellationToken ct = default) => throw new NotSupportedException();
    public IAsyncEnumerable<object?> CreateStream(object request, CancellationToken ct = default) => throw new NotSupportedException();
  }

  private sealed class RejectingRouter : IRoutingProvider
  {
    public bool IsConfigured => true;
    public int Calls { get; private set; }
    public Task<RoutePoint> GeocodeAsync(string address, CancellationToken ct)
    { Calls++; throw new InvalidOperationException("No provider call expected."); }
    public Task<TruckRoute> CalculateAsync(IReadOnlyList<RoutePoint> points, TruckRouteProfile profile, CancellationToken ct)
    { Calls++; throw new InvalidOperationException("No provider call expected."); }
  }

  private sealed class RejectingHos : IDriverHosProvider
  {
    public bool Allow { get; set; }
    public int Calls { get; private set; }
    public Task<IReadOnlyDictionary<string, DriverHosClocks>> GetClocksAsync(CancellationToken ct)
    {
      Calls++;
      if (!Allow) throw new InvalidOperationException("No HOS call expected.");
      return Task.FromResult<IReadOnlyDictionary<string, DriverHosClocks>>(new Dictionary<string, DriverHosClocks>());
    }
  }

  private sealed class ReadOnlyProbe : DbCommandInterceptor
  {
    public bool Enabled { get; private set; }
    public int Reads { get; private set; }
    public void Start() { Reads = 0; Enabled = true; }
    private void Check(DbCommand command)
    {
      if (!Enabled) return;
      Assert.StartsWith("SELECT", command.CommandText.TrimStart(), StringComparison.OrdinalIgnoreCase);
      Reads++;
    }
    public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command, CommandEventData eventData,
      InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
    { Check(command); return ValueTask.FromResult(result); }
    public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(DbCommand command, CommandEventData eventData,
      InterceptionResult<int> result, CancellationToken cancellationToken = default)
    { Check(command); return ValueTask.FromResult(result); }
    public override ValueTask<InterceptionResult<object>> ScalarExecutingAsync(DbCommand command, CommandEventData eventData,
      InterceptionResult<object> result, CancellationToken cancellationToken = default)
    { Check(command); return ValueTask.FromResult(result); }
  }
}
