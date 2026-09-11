using System.Data.Common;
using System.Reflection;
using System.Text.Json;
using Application.Features.Routing.Background;
using Application.Features.Routing.Interfaces;
using Application.Features.Routing.Models;
using Application.Features.Routing.Options;
using Application.Features.Routing.Queries;
using Application.Features.Routing.Services.Deadheads;
using Application.Features.Routing.Services.Routes;
using Domain.Entities.Dispatch;
using Domain.Entities.Fleet;
using Infrastructure.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Options;
using Load = Domain.Entities.Dispatch.Dispatch;

namespace Server.Tests.Routing;

[Trait("Category", "Routing")]
[Trait("Kind", "Integration")]
public sealed class NextLoadRouteReadTests
{
  [Theory]
  [InlineData("Versions", false)]
  [InlineData("Saved", true)]
  public void PostgreSqlSnapshotQueriesTranslateToOneJoinedRead(string method, bool geometry)
  {
    using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseNpgsql("Host=unused;Database=translation_only").Options);
    var reader = new NextLoadRouteReader(db);
    var query = (IQueryable)typeof(NextLoadRouteReader).GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic)!
      .Invoke(reader, [new[] { Guid.NewGuid(), Guid.NewGuid() }])!;
    var sql = query.ToQueryString();
    Assert.Contains("LEFT JOIN", sql);
    Assert.Contains("DispatchBaseRoutes", sql);
    Assert.Contains("DispatchDeadheads", sql);
    Assert.Equal(geometry, sql.Contains("\"RouteJson\"", StringComparison.Ordinal));
  }

  [Fact]
  public async Task ColdReadUsesOneGeometryBatchAndUnchangedOrLabelsOnlyReadsNeverFetchGeometry()
  {
    await using var fixture = await Fixture.CreateAsync();
    var first = (await fixture.Handler.Handle(new(fixture.Truck, fixture.Loads[0].Id), default)).Response!;
    Assert.Equal(2, first.Routes!.Count);
    Assert.All(first.Routes, route => Assert.Equal("ready", route.Status));
    Assert.DoesNotContain(first.Routes, route => route.Id == fixture.Loads[0].Id);
    Assert.All(first.Routes, route => Assert.NotNull(route.Deadhead));
    Assert.Equal(1, fixture.Reader.GeometryReads);
    Assert.Equal(0, fixture.Reader.VersionReads);
    fixture.Probe.Commands.Clear();
    var unchanged = (await fixture.Handler.Handle(new(fixture.Truck, fixture.Loads[0].Id, first.Revision), default)).Response!;
    Assert.True(unchanged.Unchanged);
    Assert.Null(unchanged.Routes);
    Assert.Equal(1, fixture.Reader.GeometryReads);
    Assert.Equal(1, fixture.Reader.VersionReads);
    Assert.DoesNotContain(fixture.Probe.Commands, sql => sql.Contains("\"RouteJson\"", StringComparison.Ordinal));
    fixture.Loads[1].Stops[0].Name = "Updated pickup company";
    await fixture.Db.SaveChangesAsync();
    var labels = (await fixture.Handler.Handle(new(fixture.Truck, fixture.Loads[0].Id, first.Revision), default)).Response!;
    Assert.False(labels.Unchanged);
    Assert.Null(labels.Routes);
    Assert.Contains(labels.Labels!, label => label.Names.Contains("Updated pickup company"));
    Assert.NotEqual(first.Revision, labels.Revision);
    Assert.Equal(1, fixture.Reader.GeometryReads);
  }

  [Fact]
  public async Task InsertedDispatchInvalidatesSuccessorConnectionAndMissingRoutesKeepTheirStopNumbers()
  {
    await using var fixture = await Fixture.CreateAsync();
    var original = (await fixture.Handler.Handle(new(fixture.Truck, fixture.Loads[0].Id), default)).Response!;
    var inserted = Fixture.Load(fixture.Truck, 4, 1);
    fixture.Db.Dispatches.Add(inserted);
    await fixture.Db.SaveChangesAsync();
    var changed = (await fixture.Handler.Handle(new(fixture.Truck, fixture.Loads[0].Id, original.Revision), default)).Response!;
    Assert.NotEqual(original.Revision, changed.Revision);
    var pending = changed.Routes!.Single(x => x.Id == inserted.Id);
    Assert.Equal("pending", pending.Status);
    Assert.Equal(2, pending.StopCount);
    Assert.Null(changed.Routes!.Single(x => x.Id == fixture.Loads[1].Id).Deadhead);
    Assert.NotNull(changed.Routes!.Single(x => x.Id == fixture.Loads[2].Id).Deadhead);
    var profile = await fixture.Services.Routes.ProfileAsync(fixture.Truck, default);
    profile.HeightFeet += .5;
    await new TruckPlanningProfileService(fixture.Db, fixture.Services.Reads, fixture.Services.Settings).SaveAsync(fixture.Truck, profile, default);
    var resized = (await fixture.Handler.Handle(new(fixture.Truck, fixture.Loads[0].Id, changed.Revision), default)).Response!;
    Assert.All(resized.Routes!, route => { Assert.Equal("pending", route.Status); Assert.Null(route.Deadhead); });
  }

  [Theory]
  [InlineData("{")]
  [InlineData("{\"legs\":null}")]
  public async Task CorruptBaseGeometryReturnsPendingAndQueuesRepair(string json)
  {
    await using var fixture = await Fixture.CreateAsync();
    var id = fixture.Loads[1].Id;
    (await fixture.Db.DispatchBaseRoutes.SingleAsync(x => x.DispatchId == id)).RouteJson = json;
    await fixture.Db.SaveChangesAsync();
    var response = (await fixture.Handler.Handle(new(fixture.Truck, fixture.Loads[0].Id), default)).Response!;
    var route = response.Routes!.Single(x => x.Id == id);
    Assert.Equal("pending", route.Status);
    Assert.Equal(2, route.StopCount);
    Assert.Empty(route.Stops);
    Assert.Equal(id, Assert.Single(fixture.Queue.Take(10)).DispatchId);
  }

  [Theory]
  [InlineData(null)]
  [InlineData("{")]
  [InlineData("{\"legs\":null}")]
  public async Task MissingOrCorruptDeadheadGeometryKeepsTheBaseRouteAndQueuesRepair(string? json)
  {
    await using var fixture = await Fixture.CreateAsync();
    var id = fixture.Loads[1].Id;
    (await fixture.Db.DispatchDeadheads.SingleAsync(x => x.DispatchId == id)).RouteJson = json;
    await fixture.Db.SaveChangesAsync();
    var response = (await fixture.Handler.Handle(new(fixture.Truck, fixture.Loads[0].Id), default)).Response!;
    var route = response.Routes!.Single(x => x.Id == id);
    Assert.Equal("ready", route.Status);
    Assert.Null(route.Deadhead);
    Assert.Equal(id, Assert.Single(fixture.Queue.Take(10)).DispatchId);
  }

  private sealed class Fixture(SqliteConnection connection, AppDbContext db, Probe probe, Guid truck, List<Load> loads,
    PlanningTestServices services, CountingReader reader, GetNextLoadRoutesHandler handler, RoutePreparationQueue queue) : IAsyncDisposable
  {
    public AppDbContext Db => db;
    public Probe Probe => probe;
    public Guid Truck => truck;
    public List<Load> Loads => loads;
    public PlanningTestServices Services => services;
    public CountingReader Reader => reader;
    public GetNextLoadRoutesHandler Handler => handler;
    public RoutePreparationQueue Queue => queue;
    public static async Task<Fixture> CreateAsync()
    {
      var connection = new SqliteConnection("Data Source=:memory:");
      await connection.OpenAsync();
      var probe = new Probe();
      var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connection).AddInterceptors(probe).Options);
      await db.Database.EnsureCreatedAsync();
      var truck = new Truck { Id = Guid.NewGuid() };
      db.Trucks.Add(truck);
      var loads = new[] { Load(truck.Id, 1, 0), Load(truck.Id, 2, 2), Load(truck.Id, 3, 4) }.ToList();
      loads[0].Status = "in_transit";
      db.Dispatches.AddRange(loads);
      await db.SaveChangesAsync();
      var services = new PlanningTestServices(db);
      var profile = await services.Routes.ProfileAsync(truck.Id, default);
      var stored = await db.Dispatches.AsNoTracking().Include(x => x.Stops).OrderBy(x => x.LoadNumber).ToListAsync();
      foreach (var load in stored.Skip(1))
      {
        var route = new TruckRoute { CalculatedAt = DateTime.UtcNow, Miles = 100, Seconds = 6000,
          Legs = [new(100, 6000, [new(40, -80), new(41, -80)])] };
        db.DispatchBaseRoutes.Add(new() { DispatchId = load.Id, InputHash = BaseRouteService.Signature(load, profile),
          CalculatedAt = route.CalculatedAt, RouteJson = JsonSerializer.Serialize(route, RoutePlanningService.Json) });
        var pair = DeadheadConnection.Find(load, stored)!;
        var deadhead = new TruckRoute { CalculatedAt = route.CalculatedAt, Miles = 10, Seconds = 600,
          Legs = [new(10, 600, [new((double)pair.From.Latitude!, (double)pair.From.Longitude!),
            new((double)pair.To.Latitude!, (double)pair.To.Longitude!)])] };
        db.DispatchDeadheads.Add(new() { DispatchId = load.Id, PreviousDispatchId = pair.Previous.Id,
          InputHash = pair.Signature(profile), Miles = 10, CalculatedAt = route.CalculatedAt,
          RouteJson = JsonSerializer.Serialize(deadhead, RoutePlanningService.Json) });
      }
      await db.SaveChangesAsync();
      var reader = new CountingReader(new NextLoadRouteReader(db));
      var queue = new RoutePreparationQueue(Options.Create(new RoutePreparationOptions()), TimeProvider.System);
      return new(connection, db, probe, truck.Id, loads, services, reader,
        new(reader, new DeadheadHistoryReader(db), services.Routes, queue), queue);
    }
    public static Load Load(Guid truck, int number, int day) => new()
    {
      Id = Guid.NewGuid(), TruckId = truck, LoadNumber = number, Status = "assigned", Stops = [
        new() { Id = Guid.NewGuid(), Sequence = 1, Job = "Pick Up", Name = "Pickup", Latitude = 40, Longitude = -80,
          ScheduledDate = new DateOnly(2026, 9, 8).AddDays(day), ScheduledTime = new TimeOnly(8, 0) },
        new() { Id = Guid.NewGuid(), Sequence = 2, Job = "Drop Off", Name = "Delivery", Latitude = 41, Longitude = -80,
          ScheduledDate = new DateOnly(2026, 9, 8).AddDays(day), ScheduledTime = new TimeOnly(16, 0) }]
    };
    public async ValueTask DisposeAsync() { services.Dispose(); await db.DisposeAsync(); await connection.DisposeAsync(); }
  }

  private sealed class CountingReader(INextLoadRouteReader inner) : INextLoadRouteReader
  {
    public int VersionReads { get; private set; }
    public int GeometryReads { get; private set; }
    public Task<IReadOnlyList<Load>> ReadLoadsAsync(Guid truckId, CancellationToken ct) => inner.ReadLoadsAsync(truckId, ct);
    public Task<IReadOnlyList<NextLoadRouteVersion>> ReadVersionsAsync(IReadOnlyCollection<Guid> ids, CancellationToken ct)
    { VersionReads++; return inner.ReadVersionsAsync(ids, ct); }
    public Task<IReadOnlyDictionary<Guid, SavedNextLoadRoute>> ReadGeometryAsync(IReadOnlyCollection<Guid> ids, CancellationToken ct)
    { GeometryReads++; return inner.ReadGeometryAsync(ids, ct); }
  }

  private sealed class Probe : DbCommandInterceptor
  {
    public List<string> Commands { get; } = [];
    public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command,
      CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken ct = default)
    { Commands.Add(command.CommandText); return ValueTask.FromResult(result); }
  }
}
