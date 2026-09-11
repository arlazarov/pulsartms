using System.Data.Common;
using System.Reflection;
using System.Text.Json;
using Application.Features.Dispatch.Models;
using Application.Features.Dispatch.Queries;
using Application.Features.Eta.Models;
using Application.Features.Routing.Models;
using Application.Features.Routing.Services.Deadheads;
using Application.Features.Routing.Services.Routes;
using Domain.Entities.Dispatch;
using Domain.Entities.Fleet;
using Infrastructure.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Load = Domain.Entities.Dispatch.Dispatch;

namespace Server.Tests.Eta;

[Trait("Category", "Eta")]
[Trait("Kind", "Integration")]
public sealed class EtaChainInputTests
{
  [Fact]
  public async Task FuelRecommendationRefreshDoesNotInvalidateTheDailyAllowanceForecast()
  {
    await using var fixture = await Fixture.CreateAsync();
    var first = (await fixture.Services.EtaInputs.DescribeAsync(fixture.Truck.Id, default, fixture.Ordered))!;
    var saved = await fixture.Db.DispatchRoutePlans.SingleAsync();
    var plan = JsonSerializer.Deserialize<RoutePlan>(saved.PlanJson, RoutePlanningService.Json)!;
    plan.FuelPlan = new() { CalculatedAt = DateTime.UtcNow };
    saved.PlanJson = JsonSerializer.Serialize(plan, RoutePlanningService.Json);
    await fixture.Db.SaveChangesAsync();
    var updated = (await fixture.Services.EtaInputs.DescribeAsync(fixture.Truck.Id, default, fixture.Ordered))!;
    Assert.Equal(first.InputHash, updated.InputHash);
    Assert.Equal(first.GeometryHash, updated.GeometryHash);
  }

  [Fact]
  public async Task ColdDescriptionReadsCompactMetadataWithoutTransferringDenseGeometry()
  {
    await using var fixture = await Fixture.CreateAsync(denseRoot: true);
    var description = await fixture.Services.EtaInputs.DescribeAsync(fixture.Truck.Id, default, fixture.Ordered);
    Assert.Equal(fixture.Current.Id, description!.RootDispatchId);
    Assert.Equal(1, fixture.Probe.MetadataReads);
    Assert.Equal(0, fixture.Probe.GeometryReads);
    var metadata = (await new EtaRootRouteReader(fixture.Db).ReadAsync(fixture.Current.Id, default))!;
    Assert.Equal(fixture.Truck.Id, metadata.TruckId);
    Assert.Equal(fixture.Truck.Id, metadata.PlanTruckId);
    Assert.Equal(1, metadata.Version);
    Assert.True(JsonSerializer.SerializeToUtf8Bytes(metadata).Length < 2048);
    Assert.Equal(0, fixture.Probe.GeometryReads);
  }

  [Fact]
  public void PostgreSqlMetadataQueryProjectsOnlyCompactJson()
  {
    using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
      .UseNpgsql("Host=unused;Database=translation_only").Options);
    var query = (IQueryable)typeof(EtaRootRouteReader).GetMethod("Metadata", BindingFlags.Instance | BindingFlags.NonPublic)!
      .Invoke(new EtaRootRouteReader(db), [Guid.NewGuid()])!;
    var sql = query.ToQueryString();
    Assert.Contains("jsonb_build_object", sql);
    Assert.Contains("AS \"Value\"", sql);
    Assert.Contains("{fuelPlan,calculatedAt}", sql);
    Assert.DoesNotContain("AS \"PlanJson\"", sql);
    Assert.DoesNotContain("RouteJson", sql);
  }

  [Fact]
  public async Task CompactMetadataPreservesTrackingFuelAndRejectsWrongPlanOwnership()
  {
    await using var fixture = await Fixture.CreateAsync();
    var saved = await fixture.Db.DispatchRoutePlans.SingleAsync();
    var plan = JsonSerializer.Deserialize<RoutePlan>(saved.PlanJson, RoutePlanningService.Json)!;
    var now = DateTime.UtcNow;
    plan.Tracking = new() { AllStopsPassed = true, PassedStopIds = [fixture.Current.Stops[0].Id],
      VisitedStops = new() { [fixture.Current.Stops[0].Id] = now }, NextStopId = fixture.Current.Stops[^1].Id,
      NextStopLabel = "Delivery", OffRouteSince = now };
    plan.FuelPlan = new() { CalculatedAt = now };
    saved.PlanJson = JsonSerializer.Serialize(plan, RoutePlanningService.Json);
    await fixture.Db.SaveChangesAsync();
    var metadata = (await new EtaRootRouteReader(fixture.Db).ReadAsync(fixture.Current.Id, default))!;
    Assert.Equal(plan.Id, metadata.PlanId);
    Assert.Equal(now, metadata.FuelCalculatedAt);
    Assert.Equal(now, metadata.Tracking.OffRouteSince);
    Assert.Equal(now, metadata.Tracking.VisitedStops[fixture.Current.Stops[0].Id]);
    Assert.Equal(plan.Tracking.PassedStopIds, metadata.Tracking.PassedStopIds);
    Assert.Equal(plan.Tracking.NextStopId, metadata.Tracking.NextStopId);
    Assert.Equal("Delivery", metadata.Tracking.NextStopLabel);
    var completed = await fixture.Services.EtaInputs.DescribeAsync(fixture.Truck.Id, default, fixture.Ordered);
    Assert.Equal(fixture.Next.Id, completed!.RootDispatchId);
    plan.TruckId = Guid.NewGuid();
    saved.PlanJson = JsonSerializer.Serialize(plan, RoutePlanningService.Json);
    await fixture.Db.SaveChangesAsync();
    var wrongOwner = await fixture.Services.EtaInputs.DescribeAsync(fixture.Truck.Id, default, fixture.Ordered);
    Assert.Equal(fixture.Current.Id, wrongOwner!.RootDispatchId);
  }

  [Fact]
  public async Task WarmAndScheduleOnlyReadsReuseSavedGeometryButRefreshAppointments()
  {
    await using var fixture = await Fixture.CreateAsync();
    var first = (await fixture.Services.EtaInputs.DescribeAsync(fixture.Truck.Id, default, fixture.Ordered))!;
    var original = await fixture.Services.EtaInputs.PrepareAsync(first, default);
    Assert.Null(Assert.Single(original.Future).UnavailableReason);
    var geometryReads = fixture.Probe.GeometryReads;
    var warm = (await fixture.Services.EtaInputs.DescribeAsync(fixture.Truck.Id, default, fixture.Ordered))!;
    await fixture.Services.EtaInputs.PrepareAsync(warm, default);
    Assert.Equal(geometryReads, fixture.Probe.GeometryReads);
    fixture.Next.Stops[0].ScheduledTime = new(14, 30);
    await fixture.Db.SaveChangesAsync();
    var changed = (await fixture.Services.EtaInputs.DescribeAsync(fixture.Truck.Id, default, fixture.Ordered))!;
    var prepared = await fixture.Services.EtaInputs.PrepareAsync(changed, default);
    Assert.NotEqual(first.InputHash, changed.InputHash);
    Assert.Equal(first.GeometryHash, changed.GeometryHash);
    Assert.Equal(geometryReads, fixture.Probe.GeometryReads);
    Assert.Equal(new TimeOnly(14, 30), prepared.Future[0].Stops[0].ScheduledTime);
    Assert.Equal(fixture.Next.Stops[0].Id, prepared.Future[0].Stops[0].Id);
  }

  [Fact]
  public async Task OverlappingAppointmentsReuseTheSavedConnectionWithoutZeroingItsTravelTime()
  {
    await using var fixture = await Fixture.CreateAsync();
    fixture.Next.Stops[0].ScheduledTime = new(9, 0);
    await fixture.Db.SaveChangesAsync();

    var description = (await fixture.Services.EtaInputs.DescribeAsync(fixture.Truck.Id, default, fixture.Ordered))!;
    var prepared = await fixture.Services.EtaInputs.PrepareAsync(description, default);
    var future = Assert.Single(prepared.Future);

    Assert.Null(future.UnavailableReason);
    Assert.NotNull(future.Connection);
    Assert.True(future.Connection.HasCompleteTravelTimes);
    Assert.Equal(new TimeOnly(9, 0), future.Stops[0].ScheduledTime);
    Assert.Equal(fixture.Current.Id, description.Connections[fixture.Next.Id]!.Previous.Id);
  }

  [Fact]
  public async Task WrongSavedPredecessorCannotBecomeAZeroTimeConnection()
  {
    await using var fixture = await Fixture.CreateAsync();
    var saved = await fixture.Db.DispatchDeadheads.SingleAsync();
    saved.PreviousDispatchId = fixture.Next.Id;
    await fixture.Db.SaveChangesAsync();
    var description = (await fixture.Services.EtaInputs.DescribeAsync(fixture.Truck.Id, default, fixture.Ordered))!;
    var chain = await fixture.Services.EtaInputs.PrepareAsync(description, default);
    Assert.NotNull(Assert.Single(chain.Future).UnavailableReason);
    Assert.Null(chain.Future[0].Connection);
  }

  [Fact]
  public async Task BoardReadInvalidatesTheRootWhenAFutureAppointmentChanges()
  {
    await using var fixture = await Fixture.CreateAsync();
    var description = (await fixture.Services.EtaInputs.DescribeAsync(fixture.Truck.Id, default, fixture.Ordered))!;
    var now = DateTime.UtcNow;
    var rootEta = new DispatchEta(now, now.AddMinutes(2),
      [new(fixture.Current.Stops[^1].Id, now, "Etc/UTC", null, 0, 60, 0) { DispatchId = fixture.Current.Id }], null, []);
    var nextEta = rootEta with { Stops = [new(fixture.Next.Stops[0].Id, now.AddHours(2), "Etc/UTC", null, 0, 120, 0)
      { DispatchId = fixture.Next.Id }] };
    Assert.True(await new EtaForecastStore(fixture.Db).SaveAsync([
      new(fixture.Current.Id, fixture.Truck.Id, fixture.Current.Id, description.InputHash, description.DriverExternalId, rootEta),
      new(fixture.Next.Id, fixture.Truck.Id, fixture.Current.Id, description.InputHash, description.DriverExternalId, nextEta)], default));
    fixture.Services.EtaMemory.Demand(fixture.Current.Id, description.InputHash, now);
    fixture.Services.EtaMemory.Results[fixture.Current.Id] = new("cached", rootEta) { ChainInputHash = description.InputHash };
    var initial = await fixture.Services.Board.Handle(new(TruckId: fixture.Truck.Id, IncludeHos: false, IncludeFinancials: false), default);
    Assert.All(Assert.Single(initial.Response!.Items).Dispatches, load => Assert.NotEmpty(load.Eta!.Stops));
    fixture.Next.Stops[0].ScheduledTime = new(14, 30);
    await fixture.Db.SaveChangesAsync();
    var changed = await fixture.Services.Board.Handle(new(TruckId: fixture.Truck.Id, IncludeHos: false, IncludeFinancials: false), default);
    Assert.All(Assert.Single(changed.Response!.Items).Dispatches, load => Assert.Empty(load.Eta!.Stops));
    Assert.False(fixture.Services.EtaMemory.Results.ContainsKey(fixture.Current.Id));
    Assert.Equal([fixture.Current.Id], fixture.Services.EtaMemory.Viewed.Keys);
  }

  private sealed class Fixture(SqliteConnection connection, AppDbContext db, GeometryProbe probe,
    PlanningTestServices services, Truck truck, Load current, Load next) : IAsyncDisposable
  {
    public AppDbContext Db => db;
    public GeometryProbe Probe => probe;
    public PlanningTestServices Services => services;
    public Truck Truck => truck;
    public Load Current => current;
    public Load Next => next;
    public IReadOnlyList<DispatchResponse> Ordered => [new() { Id = current.Id, TruckId = truck.Id }, new() { Id = next.Id, TruckId = truck.Id }];

    public static async Task<Fixture> CreateAsync(bool denseRoot = false)
    {
      var connection = new SqliteConnection("Data Source=:memory:");
      await connection.OpenAsync();
      var probe = new GeometryProbe();
      var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connection).AddInterceptors(probe).Options);
      await db.Database.EnsureCreatedAsync();
      var truck = new Truck { Id = Guid.NewGuid(), ExternalId = "eta-chain", UnitNumber = "ETA", IsActive = true };
      var day = DateOnly.FromDateTime(DateTime.UtcNow);
      Load Make(int number, string status, int pickup, int delivery, decimal longitude) => new()
      {
        Id = Guid.NewGuid(), LoadNumber = number, Status = status, TruckId = truck.Id,
        ShipDate = day, DeliveryDate = day,
        Stops = [new() { Id = Guid.NewGuid(), Sequence = 1, Job = "Pick Up", TruckId = truck.Id,
            ScheduledDate = day, ScheduledTime = new(pickup, 0), Latitude = 35, Longitude = longitude },
          new() { Id = Guid.NewGuid(), Sequence = 2, Job = "Drop Off", TruckId = truck.Id,
            ScheduledDate = day, ScheduledTime = new(delivery, 0), Latitude = 35, Longitude = longitude + 1 }]
      };
      var current = Make(100, "in_transit", 8, 10, -81);
      var next = Make(101, "assigned", 14, 16, -79);
      db.Trucks.Add(truck);
      db.Dispatches.AddRange(current, next);
      await db.SaveChangesAsync();
      var services = new PlanningTestServices(db);
      var profile = await services.Routes.ProfileAsync(truck.Id, default);
      var persisted = await db.Dispatches.AsNoTracking().Include(x => x.Stops).ToDictionaryAsync(x => x.Id);
      static TruckRoute Road(double from, double to) => new()
        { Miles = 60, Seconds = 3600, Legs = [new(60, 3600, [new(35, from), new(35, to)])] };
      var plan = new RoutePlan { Id = Guid.NewGuid(), Version = 1, DispatchId = current.Id, TruckId = truck.Id,
        Profile = profile, FromCurrentPosition = true, Route = Road(-81, -80),
        Stops = [new(current.Stops[^1].Id, "Delivery", "", 2, new(35, -80)) { Job = "Drop Off" }] };
      if (denseRoot) plan.Route.Legs = [new(60, 3600,
        Enumerable.Range(0, 10_001).Select(i => new RoutePoint(35, -81 + i / 10_000d)).ToList())];
      db.DispatchRoutePlans.Add(new() { Id = plan.Id, DispatchId = current.Id, TruckId = truck.Id,
        InputHash = RoutePlanningService.HashInputs(persisted[current.Id], profile), PlanJson = JsonSerializer.Serialize(plan, RoutePlanningService.Json) });
      db.DispatchBaseRoutes.Add(new() { Id = Guid.NewGuid(), DispatchId = next.Id, CalculatedAt = DateTime.UtcNow,
        InputHash = BaseRouteService.Signature(persisted[next.Id], profile), RouteJson = JsonSerializer.Serialize(Road(-79, -78), RoutePlanningService.Json) });
      var pair = DeadheadConnection.Find(persisted[next.Id], [persisted[current.Id]])!;
      db.DispatchDeadheads.Add(new() { Id = Guid.NewGuid(), DispatchId = next.Id, PreviousDispatchId = current.Id,
        InputHash = pair.Signature(profile), Miles = 60, CalculatedAt = DateTime.UtcNow,
        RouteJson = JsonSerializer.Serialize(Road(-80, -79), RoutePlanningService.Json) });
      await db.SaveChangesAsync();
      return new(connection, db, probe, services, truck, current, next);
    }

    public async ValueTask DisposeAsync()
    {
      services.Dispose();
      await db.DisposeAsync();
      await connection.DisposeAsync();
    }
  }

  private sealed class GeometryProbe : DbCommandInterceptor
  {
    public int GeometryReads { get; private set; }
    public int MetadataReads { get; private set; }
    public override ValueTask<DbDataReader> ReaderExecutedAsync(DbCommand command, CommandExecutedEventData eventData,
      DbDataReader result, CancellationToken cancellationToken = default)
    {
      var columns = Enumerable.Range(0, result.FieldCount).Select(result.GetName).ToArray();
      if (columns.Any(x => x is "RouteJson" or "PlanJson")) GeometryReads++;
      if (columns.Contains("Value") && command.CommandText.Contains("json_object", StringComparison.Ordinal)) MetadataReads++;
      return ValueTask.FromResult(result);
    }
  }
}
