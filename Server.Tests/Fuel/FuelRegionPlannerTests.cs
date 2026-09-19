using Application.Features.Routing.Services.FuelPlanning;
using Application.Features.Routing.Exceptions;
using Application.Features.Routing.Interfaces;
using Application.Features.Routing.Options;
using Application.Features.Routing.Services;
using Application.Features.Routing.Models;
using Application.Features.Dispatch.Models;
using Application.Models;
using Microsoft.Extensions.Options;
using Domain.Entities.Dispatch;
using Domain.Entities.Fleet;
using Infrastructure.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Application.Features.Routing.Services.Deadheads;
using Application.Features.Routing.Services.Routes;
using System.Text.Json;
using Application.Features.Routing.Algorithms;

namespace Server.Tests.Fuel;

[Trait("Category", "Fuel")]
[Trait("Kind", "Integration")]
public class FuelRegionPlannerTests
{
  [Theory]
  [InlineData(-99, 50)]
  [InlineData(-97, 58)]
  public async Task ReserveUsesExplicitAccessEstimatesWithoutRouting(double longitude, double minimum)
  {
    var router = new Router();
    var profile = new TruckRouteProfile { Confirmed = true, TankGallons = 100, Mpg = 5, ReserveGallons = 10, FillPercent = 100 };
    var plan = new RoutePlan { DispatchId = Guid.NewGuid(), TruckId = Guid.NewGuid(), Route = new()
      { Miles = 100, Legs = [new(100, 6000, [new(40, -101), new(40, -100)])] } };
    var local = new PricedFuelStation(new() { StationId = Guid.NewGuid(), Name = "Expensive nearby", Point = new(40, -99.9) }, 6, 6);
    var cheap = new PricedFuelStation(new() { StationId = Guid.NewGuid(), Name = "Affordable exit", Point = new(40, longitude) }, 3, 3);
    await using var fixture = await Fixture.CreateAsync(router, new Sender(), plan);
    var planner = fixture.Planner;
    var policy = await planner.BuildAsync(plan, profile, [local, cheap], 0, default);
    Assert.True(policy.PoorArea);
    Assert.Equal(cheap.Station.StationId, policy.EscapeStationId);
    Assert.Equal(FuelAccessEstimate.DistanceMiles(RouteGeometry.Distance(new(40, -100), cheap.Station.Point)), policy.EscapeMiles);
    Assert.Equal(minimum, policy.MinimumGallons);
    Assert.Equal(profile.TankGallons * profile.FillPercent / 100, policy.TargetGallons);
    Assert.True(policy.EconomicPurchasesOnly);
    Assert.Equal(0, router.Calls);
    Assert.Null(router.Address);
    Assert.Contains("estimated", policy.Reason);
    Assert.Contains("not been road-checked", policy.Reason);
    Assert.NotEmpty(policy.Regions);
  }

  [Fact]
  public async Task MissingPricesCannotCreateAnAssumedEscapeRoute()
  {
    var router = new Router();
    var plan = new RoutePlan { Route = new() { Miles = 100, Legs = [new(100, 6000, [new(40, -101), new(40, -100)])] } };
    await using var fixture = await Fixture.CreateAsync(router, new Sender(), plan);
    var planner = fixture.Planner;
    await Assert.ThrowsAsync<RoutePlanningException>(() => planner.BuildAsync(plan, new(), [], 0, default));
    Assert.Equal(0, router.Calls);
  }

  [Theory]
  [InlineData(5.696, true)]
  [InlineData(5.4, false)]
  [InlineData(5.604, false)]
  public async Task GoodDestinationValuesExtraFuelAtReplacementPriceWithoutForcingFullPurchases(double laterPrice, bool full)
  {
    var router = new Router();
    var profile = new TruckRouteProfile { Confirmed = true, TankGallons = 250, Mpg = 5,
      ReserveGallons = 25, FillPercent = 100 };
    var plan = new RoutePlan { DispatchId = Guid.NewGuid(), TruckId = Guid.NewGuid(), Route = new()
      { Miles = 500, Legs = [new(500, 30000, [new(40, -101), new(40, -100)])] } };
    PricedFuelStation Price(double longitude, double price) => new(new() { StationId = Guid.NewGuid(),
      Point = new(40, longitude), YourPrice = price, EconomicPrice = price }, price, price);
    var purchase = Price(-100.5, 5.604);
    var prices = new[] { purchase, Price(-99.99, laterPrice), Price(-99.98, laterPrice) };
    await using var fixture = await Fixture.CreateAsync(router, new Sender(), plan);
    var policy = await fixture.Planner.BuildAsync(plan, profile, prices, 0, default);
    Assert.False(policy.PoorArea);
    Assert.Equal(40, policy.MinimumGallons);
    Assert.Equal(250, policy.TargetGallons);
    var result = FuelOptimizer.Optimize(500, 100, profile,
      [new(purchase.Station, 200, 0, 0, purchase.CashUsd, purchase.EconomicUsd)], 1, false, arrivalPolicy: policy);
    var stop = Assert.Single(result.Stops);
    Assert.Equal(full, stop.FillToTarget);
    Assert.Equal(full ? 250 : 100, stop.DepartureGallons);
    Assert.Equal(full ? 190 : 40, result.ArrivalGallons);
    Assert.Equal(0, router.Calls);
  }

  [Fact]
  public async Task NextPickupKeepsExitStationInTheAssignedDirection()
  {
    var id = Guid.NewGuid(); var nextId = Guid.NewGuid();
    var sender = new Sender { Rows = [new() { Dispatches = [new() { Id = id }, new()
      { Id = nextId, Stops = [new() { Sequence = 1, Latitude = 40, Longitude = -99 }] }] }] };
    var plan = new RoutePlan { DispatchId = id, Route = new() { Miles = 100,
      Legs = [new(100, 6000, [new(40, -101), new(40, -100)])] } };
    PricedFuelStation Station(string name, double longitude) => new(new() { StationId = Guid.NewGuid(), Name = name, Point = new(40, longitude) }, 3, 3);
    var west = Station("Wrong direction", -100.2); var east = Station("Toward pickup", -99.5);
    var router = new Router();
    var profile = new TruckRouteProfile { Confirmed = true, TankGallons = 100, Mpg = 5,
      ReserveGallons = 10, FillPercent = 100 };
    await using var fixture = await Fixture.CreateAsync(router, sender, plan);
    await fixture.SaveConnectionAsync(plan, profile, nextId);
    var planner = fixture.Planner;
    var policy = await planner.BuildAsync(plan, profile, [west, east], 0, default);
    Assert.Equal(east.Station.StationId, policy.EscapeStationId);
    Assert.Equal(nextId, policy.NextDispatchId);
    Assert.Equal(0, router.Calls);
  }

  [Theory]
  [InlineData(true)]
  [InlineData(false)]
  public async Task UnverifiedNextPickupDoesNotStartGeocodingOrUseImportedCoordinates(bool hasImportedCoordinates)
  {
    var id = Guid.NewGuid();
    var pickup = new DispatchStopResponse { Sequence = 1, Address = "100 Main St", City = "Town", Province = "KS",
      ZipCode = "66000", Country = "US", Latitude = hasImportedCoordinates ? 40 : null,
      Longitude = hasImportedCoordinates ? -101 : null };
    var sender = new Sender { Rows = [new() { Dispatches = [new() { Id = id }, new() { Id = Guid.NewGuid(), Stops = [pickup] }] }] };
    var router = new Router();
    var plan = new RoutePlan { DispatchId = id, Route = new() { Miles = 100,
      Legs = [new(100, 6000, [new(40, -101), new(40, -100)])] } };
    var station = new PricedFuelStation(new() { StationId = Guid.NewGuid(), Point = new(40, -99.5) }, 3, 3);
    await using var fixture = await Fixture.CreateAsync(router, sender, plan);
    var error = await Assert.ThrowsAsync<RoutePlanningException>(() => fixture.Planner.BuildAsync(plan,
      new() { Confirmed = true, TankGallons = 100, Mpg = 5, ReserveGallons = 10, FillPercent = 100 }, [station], 0, default));
    Assert.Contains("confirmed saved stop location", error.Message);
    Assert.Null(router.Address);
    Assert.Equal(0, router.Calls);
  }

  [Theory]
  [InlineData("matching", true)]
  [InlineData("profile", false)]
  [InlineData("intermediate", false)]
  [InlineData("incomplete", false)]
  [InlineData("missing", false)]
  public async Task OnlyMatchingSavedDeadheadsSupplyOnwardGeometryAndMissingDataNeverRoutes(string scenario, bool usable)
  {
    var id = Guid.NewGuid(); var nextId = Guid.NewGuid();
    var sender = new Sender { Rows = [new() { Dispatches = [new() { Id = id }, new()
      { Id = nextId, Stops = [new() { Sequence = 1, Latitude = 40, Longitude = -99 }] }] }] };
    var plan = new RoutePlan { DispatchId = id, Route = new() { Miles = 100,
      Legs = [new(100, 6000, [new(40, -101), new(40, -100)])] } };
    var profile = new TruckRouteProfile { Confirmed = true, TankGallons = 100, Mpg = 5, ReserveGallons = 10, FillPercent = 100 };
    var router = new Router();
    await using var fixture = await Fixture.CreateAsync(router, sender, plan);
    var history = await fixture.Services.Deadheads.ReadHistoryAsync([nextId], default);
    var pair = Assert.IsType<DeadheadConnection>(DeadheadConnection.Find(history[nextId]));
    var route = new TruckRoute { Miles = 100, Seconds = 6000, Legs = [new(100, 6000, [new(40, -100), new(40, -99)])] };
    if (scenario == "incomplete") route.Legs.Clear();
    if (scenario != "missing")
      fixture.Db.DispatchDeadheads.Add(new() { Id = Guid.NewGuid(), DispatchId = nextId, PreviousDispatchId = id,
        InputHash = pair.Signature(profile), Miles = 100, RouteJson = JsonSerializer.Serialize(route, RoutePlanningService.Json) });
    if (scenario == "intermediate")
    {
      var date = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(1);
      fixture.Db.Dispatches.Add(new() { Id = Guid.NewGuid(), LoadNumber = 100, TruckId = plan.TruckId, Status = "assigned", ShipDate = date, DeliveryDate = date,
        Stops = [new() { Id = Guid.NewGuid(), Sequence = 1, Job = "Pick Up", ScheduledDate = date, Latitude = 40, Longitude = -99.8m },
          new() { Id = Guid.NewGuid(), Sequence = 2, Job = "Drop Off", ScheduledDate = date, Latitude = 40, Longitude = -99.7m }] });
    }
    await fixture.Db.SaveChangesAsync();
    if (scenario == "profile") profile.HeightFeet = 14;
    var station = new PricedFuelStation(new() { StationId = Guid.NewGuid(), Point = new(40, -99.5) }, 3, 3);
    if (usable) await fixture.Planner.BuildAsync(plan, profile, [station], 0, default);
    else
    {
      var error = await Assert.ThrowsAsync<RoutePlanningException>(() => fixture.Planner.BuildAsync(plan, profile, [station], 0, default));
      Assert.Contains("saved connection", error.Message);
    }
    Assert.Equal(0, router.Calls);
    Assert.Null(router.Address);
  }

  [Theory]
  [InlineData(0, true)]
  [InlineData(30, false)]
  public async Task StreetProvenanceMustBeCurrentEvenWhenConnectionGeometryExists(int verifiedDaysAgo, bool usable)
  {
    var id = Guid.NewGuid(); var nextId = Guid.NewGuid();
    var sender = new Sender { Rows = [new() { Dispatches = [new() { Id = id }, new()
      { Id = nextId, Stops = [new() { Sequence = 1, Address = "100 Main St", Latitude = 40, Longitude = -99 }] }] }] };
    var plan = new RoutePlan { DispatchId = id, Route = new() { Miles = 100,
      Legs = [new(100, 6000, [new(40, -101), new(40, -100)])] } };
    var profile = new TruckRouteProfile { Confirmed = true, TankGallons = 100, Mpg = 5,
      ReserveGallons = 10, FillPercent = 100 };
    var router = new Router();
    await using var fixture = await Fixture.CreateAsync(router, sender, plan);
    var pickup = await fixture.Db.DispatchStops.SingleAsync(stop => stop.DispatchId == nextId);
    pickup.AddressVerifiedAt = DateTime.UtcNow.AddDays(-verifiedDaysAgo).AddMinutes(-1);
    pickup.SourceAddressJson = StopAddress.From(pickup).Serialize();
    await fixture.Db.SaveChangesAsync();
    await fixture.SaveConnectionAsync(plan, profile, nextId);
    var station = new PricedFuelStation(new() { StationId = Guid.NewGuid(), Point = new(40, -99.5) }, 3, 3);

    if (usable) await fixture.Planner.BuildAsync(plan, profile, [station], 0, default);
    else await Assert.ThrowsAsync<RoutePlanningException>(() => fixture.Planner.BuildAsync(plan, profile, [station], 0, default));

    Assert.Equal(0, router.Calls);
    Assert.Null(router.Address);
  }

  private sealed class Fixture(SqliteConnection connection, AppDbContext db, PlanningTestServices services,
    FuelRegionPlanner planner) : IAsyncDisposable
  {
    public AppDbContext Db => db;
    public PlanningTestServices Services => services;
    public FuelRegionPlanner Planner => planner;
    public async Task SaveConnectionAsync(RoutePlan plan, TruckRouteProfile profile, Guid nextId)
    {
      var history = await services.Deadheads.ReadHistoryAsync([nextId], default);
      var pair = Assert.IsType<DeadheadConnection>(DeadheadConnection.Find(history[nextId]));
      var route = new TruckRoute { Miles = 100, Seconds = 6000,
        Legs = [new(100, 6000, [plan.Route.Legs[^1].Points[^1], new((double)pair.To.Latitude!, (double)pair.To.Longitude!)])] };
      db.DispatchDeadheads.Add(new() { Id = Guid.NewGuid(), DispatchId = nextId, PreviousDispatchId = plan.DispatchId,
        InputHash = pair.Signature(profile), Miles = 100, RouteJson = JsonSerializer.Serialize(route, RoutePlanningService.Json) });
      await db.SaveChangesAsync();
    }
    public static async Task<Fixture> CreateAsync(Router router, Sender sender, RoutePlan plan)
    {
      var connection = new SqliteConnection("Data Source=:memory:");
      await connection.OpenAsync();
      var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connection).Options);
      await db.Database.EnsureCreatedAsync();
      if (plan.TruckId == Guid.Empty) plan.TruckId = Guid.NewGuid();
      db.Trucks.Add(new Truck { Id = plan.TruckId, ExternalId = "fuel-region", UnitNumber = "Fuel", IsActive = true });
      var index = 0;
      foreach (var row in sender.Rows)
      {
        row.TruckId = plan.TruckId;
        foreach (var load in row.Dispatches)
        {
          load.TruckId = plan.TruckId;
          var date = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(index++ * 2);
          var stops = load.Stops.Select(stop => new DispatchStop
          {
            Id = Guid.NewGuid(), Sequence = stop.Sequence, TruckId = plan.TruckId,
            Job = stop.Sequence == 1 ? "Pick Up" : "Drop Off", Address = stop.Address,
            City = stop.City, Province = stop.Province, Country = stop.Country, ZipCode = stop.ZipCode,
            Latitude = stop.Latitude, Longitude = stop.Longitude, ScheduledDate = date
          }).ToList();
          if (stops.Count == 0)
            stops = [new() { Id = Guid.NewGuid(), TruckId = plan.TruckId, Sequence = 1, Job = "Pick Up", ScheduledDate = date,
              Latitude = 40, Longitude = -101 },
              new() { Id = Guid.NewGuid(), TruckId = plan.TruckId, Sequence = 2, Job = "Drop Off", ScheduledDate = date,
                Latitude = 40, Longitude = -100 }];
          db.Dispatches.Add(new() { Id = load.Id, LoadNumber = index, TruckId = plan.TruckId, Status = index == 1 ? "in_transit" : "assigned",
            ShipDate = date, DeliveryDate = date, Stops = stops });
        }
      }
      await db.SaveChangesAsync();
      var services = new PlanningTestServices(db, router);
      return new(connection, db, services, new(sender, Options.Create(new FuelRegionOptions()), services.Routes, services.Deadheads));
    }
    public async ValueTask DisposeAsync()
    {
      services.Dispose();
      await db.DisposeAsync();
      await connection.DisposeAsync();
    }
  }

  private sealed class Router : IRoutingProvider
  {
    public bool IsConfigured => true;
    public int Calls;
    public string? Address;
    public Task<TruckRoute> CalculateAsync(IReadOnlyList<RoutePoint> points, TruckRouteProfile profile, CancellationToken ct)
    {
      Calls++;
      throw new InvalidOperationException("Fuel region estimates must not request a road.");
    }
    public Task<RoutePoint> GeocodeAsync(string address, CancellationToken ct)
    {
      Address = address;
      throw new InvalidOperationException("Fuel region estimates must not geocode a stop.");
    }
  }

  private sealed class Sender : Application.Features.Dispatch.Interfaces.IDispatchBoardReader
  {
    public List<TruckDispatchBoardResponse> Rows { get; set; } = [];
    public Task<PaginatedList<TruckDispatchBoardResponse>> ReadAsync(Application.Features.Dispatch.Queries.GetDispatchBoardQuery request, CancellationToken ct) =>
      Task.FromResult(new PaginatedList<TruckDispatchBoardResponse> { Items = Rows, Page = 1, PageSize = 12 });
  }
}
