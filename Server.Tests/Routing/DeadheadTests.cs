using Application.Features.Dispatch.Models;
using Application.Features.Dispatch.Services;
using Application.Features.Routing.Interfaces;
using Application.Features.Routing.Services;
using Application.Features.Routing.Services.Deadheads;
using Domain.Entities.Dispatch;
using Domain.Entities.Fleet;
using Domain.Models.Routing;
using Infrastructure.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Server.Tests.Routing;

using Dispatch = global::Domain.Entities.Dispatch.Dispatch;

[Trait("Category", "Finance")]
[Trait("Kind", "Integration")]
public sealed class DeadheadTests
{
  [Fact]
  public async Task SavedMileageIsReusedAndInsertionRecalculatesDestination()
  {
    await using var connection = new SqliteConnection("Data Source=:memory:");
    await connection.OpenAsync();
    await using var db = new AppDbContext(
      new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connection).Options
    );
    await db.Database.EnsureCreatedAsync();
    var truck = new Truck { Id = Guid.NewGuid() };
    db.Trucks.Add(truck);
    var a = Load(truck.Id, 1);
    var b = Load(truck.Id, 5);
    b.Price = 1000;
    b.LoadedMiles = 400;
    db.Dispatches.AddRange(a, b);
    await db.SaveChangesAsync();
    var router = new Router();
    using var services = new PlanningTestServices(db, router);
    var plans = services.Routes;
    var service = services.Deadheads;
    var profile = await plans.ProfileAsync(truck.Id, default);
    await service.EnsureAsync(b, profile, default);
    var saved = await db.DispatchDeadheads.SingleAsync();
    saved.CalculatedAt = DateTime.UtcNow.AddYears(-1);
    await db.SaveChangesAsync();
    await service.EnsureAsync(b, profile, default);
    Assert.Equal(1, router.Calls);
    var rates = await db.DispatchRates.SingleAsync();
    Assert.Equal(2.5m, rates.LoadedRatePerMile);
    Assert.Equal(2m, rates.TotalRatePerMile);
    var card = new DispatchResponse
    {
      Id = b.Id,
      TruckId = truck.Id,
      Price = b.Price,
      LoadedMiles = b.LoadedMiles,
      Currency = b.Currency,
    };
    await service.ReadAsync([card], default);
    Assert.Equal(2m, card.TotalRatePerMile);
    b.Price = 2000;
    await db.SaveChangesAsync();
    Assert.False(
      DispatchRates.Matches(
        rates,
        new(b.Id, b.Price, b.LoadedMiles, b.Currency),
        saved.Miles,
        saved.InputHash
      )
    );
    card.Price = b.Price;
    await service.ReadAsync([card], default);
    Assert.Equal(4m, card.TotalRatePerMile);
    Assert.Equal(1, router.Calls);
    await service.EnsureAsync(b, profile, default);
    Assert.Equal(1, router.Calls);
    Assert.Equal(4m, rates.TotalRatePerMile);
    var c = Load(truck.Id, 3);
    db.Dispatches.Add(c);
    await db.SaveChangesAsync();
    await service.EnsureAsync(b, profile, default);
    Assert.Equal(2, router.Calls);
    Assert.Equal(c.Id, saved.PreviousDispatchId);
    Assert.Equal(100m, saved.Miles);
    saved.Miles = null;
    saved.RetryAfter = DateTime.UtcNow.AddMinutes(5);
    await db.SaveChangesAsync();
    await service.EnsureAsync(b, profile, default);
    Assert.Equal(2, router.Calls);
  }

  private static Dispatch Load(Guid truck, int day) =>
    new()
    {
      Id = Guid.NewGuid(),
      LoadNumber = day,
      TruckId = truck,
      Status = "assigned",
      Stops =
      [
        new()
        {
          Id = Guid.NewGuid(),
          Sequence = 1,
          Job = "Pick Up",
          ScheduledDate = new(2026, 9, day),
          Latitude = 40,
          Longitude = -80,
        },
        new()
        {
          Id = Guid.NewGuid(),
          Sequence = 2,
          Job = "Drop Off",
          ScheduledDate = new(2026, 9, day + 1),
          Latitude = 41,
          Longitude = -79,
        },
      ],
    };

  private sealed class Router : IRoutingProvider
  {
    public bool IsConfigured => true;
    public int Calls { get; private set; }

    public Task<RoutePoint> GeocodeAsync(
      string address,
      CancellationToken ct
    ) => throw new NotSupportedException();

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
          Legs = [new(100, 0, points.ToList())],
        }
      );
    }
  }
}
