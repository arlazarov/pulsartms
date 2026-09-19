using Application.Features.Dispatch.Models;
using Application.Features.Routing.Interfaces;
using Application.Features.Routing.Models;
using Application.Features.Routing.Services.Deadheads;
using Application.Features.Routing.Services.Routes;
using Domain.Entities.Fleet;
using Infrastructure.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Server.Tests.Eta;

using Dispatch = global::Domain.Entities.Dispatch.Dispatch;

[Trait("Category", "Eta")]
[Trait("Kind", "Integration")]
public sealed class EtaDescriptionMemoTests
{
  [Fact]
  public async Task BoardEnrichmentReusesTheDescriptionUntilAnInputGenerationChanges()
  {
    await using var fixture = await SavedFuelHorizonFixture.CreateAsync();
    var truckId = fixture.Current.TruckId!.Value;
    var reads = fixture.Services.Reads;
    IReadOnlyList<DispatchResponse> ordered = [new() { Id = fixture.Current.Id }, new() { Id = fixture.Future.Id }];
    var inputs = fixture.Services.EtaInputs;
    var first = (await inputs.DescribeAsync(truckId, default, ordered, memoized: true))!;
    Assert.Same(first, await inputs.DescribeAsync(truckId, default, ordered, memoized: true));
    var fresh = (await inputs.DescribeAsync(truckId, default, ordered))!;
    Assert.NotSame(first, fresh);
    Assert.Equal(first.InputHash, fresh.InputHash);
    Assert.Same(first, await inputs.DescribeAsync(truckId, default, ordered, memoized: true));

    var previous = first;
    foreach (var group in new[] { "dispatch", "board", $"profile:{truckId}", $"route:{fixture.Current.Id}", $"chain:{fixture.Future.Id}" })
    {
      reads.Invalidate(group);
      var described = (await inputs.DescribeAsync(truckId, default, ordered, memoized: true))!;
      Assert.NotSame(previous, described);
      Assert.Equal(first.InputHash, described.InputHash);
      previous = described;
    }
    IReadOnlyList<DispatchResponse> currentOnly = [new() { Id = fixture.Current.Id }];
    Assert.NotSame(previous, await inputs.DescribeAsync(truckId, default, currentOnly, memoized: true));
  }

  [Fact]
  public async Task RefreshWorkersNeverReadTheMemo()
  {
    await using var fixture = await SavedFuelHorizonFixture.CreateAsync();
    var truckId = fixture.Current.TruckId!.Value;
    var inputs = fixture.Services.EtaInputs;
    IReadOnlyList<DispatchResponse> ordered = [new() { Id = fixture.Current.Id }, new() { Id = fixture.Future.Id }];
    var remembered = (await inputs.DescribeAsync(truckId, default, ordered, memoized: true))!;
    var worker = (await inputs.DescribeAsync(truckId, default))!;
    Assert.NotSame(remembered, worker);
    Assert.NotSame(worker, await inputs.DescribeAsync(truckId, default));
    Assert.Equal(remembered.InputHash, worker.InputHash);
  }

  [Fact]
  public async Task SavingABaseRouteBumpsTheChainGeneration()
  {
    await using var connection = new SqliteConnection("Data Source=:memory:");
    await connection.OpenAsync();
    await using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connection).Options);
    await db.Database.EnsureCreatedAsync();
    var load = new Dispatch { Id = Guid.NewGuid(), Stops = [
      new() { Id = Guid.NewGuid(), Sequence = 1, Latitude = 40, Longitude = -80 },
      new() { Id = Guid.NewGuid(), Sequence = 2, Latitude = 41, Longitude = -79 }] };
    db.Dispatches.Add(load);
    await db.SaveChangesAsync();
    using var reads = TestCache.Create();
    using var gates = new Application.Caching.ProcessGates();
    var before = reads.Generation($"chain:{load.Id}");
    await new BaseRouteService(db, new Router(), reads, gates).EnsureAsync(load, new TruckRouteProfile { UsesFleetDefaults = true }, default);
    Assert.NotEqual(before, reads.Generation($"chain:{load.Id}"));
  }

  [Fact]
  public async Task SavingADeadheadBumpsTheChainGeneration()
  {
    await using var connection = new SqliteConnection("Data Source=:memory:");
    await connection.OpenAsync();
    await using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connection).Options);
    await db.Database.EnsureCreatedAsync();
    var truck = new Truck { Id = Guid.NewGuid() };
    db.Trucks.Add(truck);
    var earlier = Load(truck.Id, 1);
    var later = Load(truck.Id, 5);
    db.Dispatches.AddRange(earlier, later);
    await db.SaveChangesAsync();
    var router = new Router();
    using var services = new PlanningTestServices(db, router);
    var reads = services.Reads;
    var before = reads.Generation($"chain:{later.Id}");
    var service = new DeadheadService(db, router, services.Routes, new(db), new DeadheadHistoryReader(db), reads, services.Gates);
    await service.EnsureAsync(later, await services.Routes.ProfileAsync(truck.Id, default), default);
    Assert.Equal(1, router.Calls);
    Assert.NotEqual(before, reads.Generation($"chain:{later.Id}"));
  }

  private static Dispatch Load(Guid truck, int day) => new()
  {
    Id = Guid.NewGuid(), LoadNumber = day, TruckId = truck, Status = "assigned", Stops = [
      new() { Id = Guid.NewGuid(), Sequence = 1, Job = "Pick Up", ScheduledDate = new(2026, 9, day), Latitude = 40, Longitude = -80 },
      new() { Id = Guid.NewGuid(), Sequence = 2, Job = "Drop Off", ScheduledDate = new(2026, 9, day + 1), Latitude = 41, Longitude = -79 }
    ]
  };

  private sealed class Router : IRoutingProvider
  {
    public bool IsConfigured => true;
    public int Calls { get; private set; }
    public Task<RoutePoint> GeocodeAsync(string address, CancellationToken ct) => throw new NotSupportedException();
    public Task<TruckRoute> CalculateAsync(IReadOnlyList<RoutePoint> points, TruckRouteProfile profile, CancellationToken ct)
    {
      Calls++;
      return Task.FromResult(new TruckRoute { Points = points.ToList(), Miles = 100, Legs = [new(100, 0, points.ToList())] });
    }
  }
}
