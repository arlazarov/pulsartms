using Application.Features.Eta.Interfaces;
using Application.Features.Eta.Models;
using Application.Features.Eta.Options;
using Application.Features.Eta.Services;
using Application.Features.Fleet.Interfaces;
using Application.Features.Fleet.Models;
using Application.Features.Routing.Models;
using Domain.Entities.Execution;
using Domain.Entities.Fleet;
using Infrastructure.Integrations.GeoTimeZone;
using Infrastructure.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Server.Tests.Eta;

[Trait("Category", "Eta")]
[Trait("Kind", "Integration")]
public sealed class EtaNativeDriverTests
{
  [Theory]
  [InlineData("native", "native-driver")]
  [InlineData("legacy", "catalog-driver")]
  [InlineData("stale-revision", null)]
  [InlineData("planned", "native-driver")]
  [InlineData("completed", null)]
  [InlineData("cancelled", null)]
  [InlineData("no-driver", null)]
  [InlineData("wrong-truck", null)]
  public async Task NativeRouteUsesOnlyItsOpenAssignmentDriver(
    string scenario,
    string? expectedDriver
  )
  {
    await using var connection = new SqliteConnection("Data Source=:memory:");
    await connection.OpenAsync();
    await using var db = new AppDbContext(
      new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connection).Options
    );
    await db.Database.EnsureCreatedAsync();
    var catalog = new Driver
    {
      Id = Guid.NewGuid(),
      ExternalId = "catalog-driver",
      Name = "Catalog driver",
    };
    var assigned = new Driver
    {
      Id = Guid.NewGuid(),
      ExternalId = "native-driver",
      Name = "Actual execution driver",
    };
    var truck = new Truck
    {
      Id = Guid.NewGuid(),
      ExternalId = "eta-native-truck",
      UnitNumber = "54777",
      DriverId = catalog.Id,
      Driver = catalog,
    };
    var trip = new Trip { Id = Guid.NewGuid() };
    var leg = new ExecutionLeg
    {
      Id = Guid.NewGuid(),
      TripId = trip.Id,
      Trip = trip,
      TruckId = truck.Id,
      DriverId = scenario == "no-driver" ? null : assigned.Id,
      Status = scenario is "planned" or "completed" or "cancelled"
        ? scenario
        : "active",
      Revision = 4,
    };
    db.Drivers.AddRange(catalog, assigned);
    db.Trucks.Add(truck);
    db.Trips.Add(trip);
    db.ExecutionLegs.Add(leg);
    await db.SaveChangesAsync();
    var hos = new HosSpy();
    using var memory = new EtaMemory();
    var service = new EtaService(
      db,
      hos,
      new RouteRegionLookup(),
      memory,
      hos,
      Options.Create(new EtaPlanningOptions())
    );
    var plan = new RoutePlan
    {
      Id = Guid.NewGuid(),
      DispatchId = Guid.NewGuid(),
      TruckId = scenario == "wrong-truck" ? Guid.NewGuid() : truck.Id,
      ExecutionLegId = scenario == "legacy" ? null : leg.Id,
      AssignmentRevision = scenario == "stale-revision" ? 3 : 4,
      Version = 1,
    };
    var state = new RoutePlanningState(new(), plan, null, null, null, true);

    var result = await service.GetAsync(state, default);

    Assert.NotNull(result);
    if (expectedDriver is null)
      Assert.Empty(hos.HistoryDrivers);
    else
      Assert.Equal(expectedDriver, Assert.Single(hos.HistoryDrivers));
    Assert.Equal(1, hos.ClockCalls);
    var scope = memory.Scope(plan.DispatchId, plan.ExecutionLegId);
    Assert.Same(result, memory.Results[scope].Value);
    Assert.Equal(plan.DispatchId, memory.Resolve(scope).DispatchId);
    if (plan.ExecutionLegId.HasValue)
      Assert.False(memory.Results.ContainsKey(plan.DispatchId));
  }

  private sealed class HosSpy : IDriverHosProvider, IHosHistoryProvider
  {
    public int ClockCalls { get; private set; }
    public List<string> HistoryDrivers { get; } = [];

    public Task<IReadOnlyDictionary<string, DriverHosClocks>> GetClocksAsync(
      CancellationToken ct
    )
    {
      ClockCalls++;
      return Task.FromResult<IReadOnlyDictionary<string, DriverHosClocks>>(
        new Dictionary<string, DriverHosClocks>
        {
          ["catalog-driver"] = new() { CycleMs = 3_600_000 },
          ["native-driver"] = new() { CycleMs = 20 * 3_600_000 },
        }
      );
    }

    public Task<HosHistory?> GetAsync(string driverId, CancellationToken ct)
    {
      HistoryDrivers.Add(driverId);
      return Task.FromResult<HosHistory?>(null);
    }
  }
}
