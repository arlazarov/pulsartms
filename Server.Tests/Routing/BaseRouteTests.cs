using System.Text.Json;
using Application.Features.Routing.Interfaces;
using Application.Features.Routing.Models;
using Application.Features.Routing.Services;
using Application.Features.Routing.Services.Routes;
using Domain.Entities.Dispatch;
using Domain.Entities.Fleet;
using Infrastructure.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Server.Tests.Support;

namespace Server.Tests.Routing;

using Dispatch = global::Domain.Entities.Dispatch.Dispatch;

[Trait("Category", "Routing")]
[Trait("Kind", "Integration")]
public sealed class BaseRouteTests
{
  [Fact]
  public async Task UnassignedBaseRouteSurvivesAgeAndAssignmentButChangesWithDestination()
  {
    await using var connection = new SqliteConnection("Data Source=:memory:");
    await connection.OpenAsync();
    await using var db = new AppDbContext(
      new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connection).Options
    );
    await db.Database.EnsureCreatedAsync();
    var load = new Dispatch
    {
      Id = Guid.NewGuid(),
      Stops =
      [
        new()
        {
          Id = Guid.NewGuid(),
          Sequence = 1,
          Latitude = 40,
          Longitude = -80,
        },
        new()
        {
          Id = Guid.NewGuid(),
          Sequence = 2,
          Latitude = 41,
          Longitude = -79,
        },
      ],
    };
    db.Dispatches.Add(load);
    await db.SaveChangesAsync();
    var router = new Router();
    using var services = new PlanningTestServices(db, router);
    var service = services.BaseRoutes;
    var profile = new TruckRouteProfile { UsesFleetDefaults = true };
    await service.EnsureAsync(load, profile, default);
    var saved = await db.DispatchBaseRoutes.SingleAsync();
    saved.CalculatedAt = DateTime.UtcNow.AddYears(-1);
    await db.SaveChangesAsync();
    db.ChangeTracker.Clear();
    var truck = new Truck { Id = Guid.NewGuid(), ExternalId = "base-assigned" };
    db.Trucks.Add(truck);
    await db.SaveChangesAsync();
    load.TruckId = truck.Id;
    await db
      .Dispatches.Where(x => x.Id == load.Id)
      .ExecuteUpdateAsync(s => s.SetProperty(x => x.TruckId, truck.Id));
    await service.EnsureAsync(load, profile, default);
    Assert.Equal(1, router.Calls);
    load.Stops[1].Longitude = -78;
    await db
      .DispatchStops.Where(x => x.Id == load.Stops[1].Id)
      .ExecuteUpdateAsync(s => s.SetProperty(x => x.Longitude, -78m));
    await service.EnsureAsync(load, profile, default);
    Assert.Equal(2, router.Calls);
    Assert.Empty(await db.DispatchRoutePlans.ToListAsync());
    profile.HeightFeet = 14;
    await PlanningProfileFixture.SaveAsync(db, truck.Id, profile);
    await service.EnsureAsync(load, profile, default);
    Assert.Equal(3, router.Calls);
  }

  [Theory]
  [InlineData("ready", 0)]
  [InlineData("changed", 1)]
  [InlineData("live", 1)]
  [InlineData("incomplete", 1)]
  [InlineData("wrong-owner", 1)]
  [InlineData("corrupt", 1)]
  public async Task MatchingCompleteNonLivePlanIsCheckedBeforeAnyGeocoding(
    string savedState,
    int calls
  )
  {
    await using var connection = new SqliteConnection("Data Source=:memory:");
    await connection.OpenAsync();
    await using var db = new AppDbContext(
      new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connection).Options
    );
    await db.Database.EnsureCreatedAsync();
    var truck = new Truck { Id = Guid.NewGuid(), ExternalId = "base-reuse" };
    var load = new Dispatch
    {
      Id = Guid.NewGuid(),
      TruckId = truck.Id,
      Stops =
      [
        new()
        {
          Id = Guid.NewGuid(),
          Sequence = 1,
          Address = "Pickup Street",
          Latitude = 40,
          Longitude = -80,
        },
        new()
        {
          Id = Guid.NewGuid(),
          Sequence = 2,
          Address = "Delivery Street",
          Latitude = 41,
          Longitude = -79,
        },
      ],
    };
    db.Trucks.Add(truck);
    db.Dispatches.Add(load);
    await db.SaveChangesAsync();
    load = await db
      .Dispatches.AsNoTracking()
      .Include(x => x.Stops)
      .SingleAsync();
    var profile = new TruckRouteProfile { UsesFleetDefaults = true };
    var plan = new RoutePlan
    {
      Id = Guid.NewGuid(),
      DispatchId = load.Id,
      TruckId = truck.Id,
      Profile = profile,
      FromCurrentPosition = savedState == "live",
      Route = new()
      {
        Miles = 100,
        Seconds = 100,
        Legs = [new(100, 100, [new(40, -80), new(41, -79)])],
      },
    };
    if (savedState == "incomplete")
      plan.Route.Legs.Clear();
    if (savedState == "wrong-owner")
      plan.TruckId = Guid.NewGuid();
    db.DispatchRoutePlans.Add(
      new()
      {
        Id = plan.Id,
        DispatchId = load.Id,
        TruckId = truck.Id,
        InputHash =
          savedState == "changed"
            ? "different"
            : RoutePlanInputs.Hash(load, profile),
        PlanJson =
          savedState == "corrupt"
            ? "{"
            : JsonSerializer.Serialize(plan, RoutingJson.Options),
      }
    );
    await db.SaveChangesAsync();
    var router = new Router();
    using var services = new PlanningTestServices(db, router);
    var route = await services.BaseRoutes.EnsureAsync(load, profile, default);
    Assert.Single(route.Legs);
    Assert.Equal(calls, router.Calls);
    Assert.Equal(calls * 2, router.Geocodes);
    Assert.Single(await db.DispatchBaseRoutes.ToListAsync());
  }

  private sealed class Router : IRoutingProvider
  {
    public bool IsConfigured => true;
    public int Calls { get; private set; }
    public int Geocodes { get; private set; }

    public Task<RoutePoint> GeocodeAsync(string address, CancellationToken ct)
    {
      Geocodes++;
      return Task.FromResult(
        address.StartsWith("Pickup", StringComparison.Ordinal)
          ? new RoutePoint(40, -80)
          : new RoutePoint(41, -79)
      );
    }

    public Task<TruckRoute> CalculateAsync(
      IReadOnlyList<RoutePoint> points,
      TruckRouteProfile profile,
      CancellationToken ct
    )
    {
      Calls++;
      return Task.FromResult(
        new TruckRoute
        {
          Points = points.ToList(),
          Miles = 100,
          Legs = points
            .Zip(
              points.Skip(1),
              (from, to) =>
                new RouteLeg(100 / (points.Count - 1d), 0, [from, to])
            )
            .ToList(),
        }
      );
    }
  }
}
