using Application.Features.Routing.Interfaces;
using Application.Features.Routing.Models;
using Application.Features.Routing.Services.Deadheads;
using Application.Features.Routing.Services.FuelPlanning;
using Application.Features.Routing.Services.Routes;
using Domain.Entities.Dispatch;
using Domain.Entities.Fleet;
using Infrastructure.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Server.Tests.Support;

using Dispatch = global::Domain.Entities.Dispatch.Dispatch;

internal sealed class SavedFuelHorizonFixture : IAsyncDisposable
{
  private SqliteConnection Connection { get; init; } = null!;
  public required AppDbContext Db { get; init; }
  public required PlanningTestServices Services { get; init; }
  public required Dispatch Current { get; init; }
  public required Dispatch Future { get; init; }
  public required RoutePlanningState State { get; init; }
  public required ForbiddenRouter Router { get; init; }
  public FuelHorizon Horizon => new(Services.Routes, Db, Services.Sender, Services.Deadheads);

  public static async Task<SavedFuelHorizonFixture> CreateAsync()
  {
    var connection = new SqliteConnection("Data Source=:memory:");
    await connection.OpenAsync();
    var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connection).Options);
    await db.Database.EnsureCreatedAsync();
    var today = DateOnly.FromDateTime(DateTime.UtcNow);
    var truck = new Truck { Id = Guid.NewGuid(), ExternalId = "saved-fuel-horizon", UnitNumber = "Fuel", IsActive = true };
    Dispatch Load(int number, DateOnly date, decimal from, decimal to) => new()
    {
      Id = Guid.NewGuid(), Truck = truck, LoadNumber = number, Status = number == 1 ? "in_transit" : "assigned",
      ShipDate = date, DeliveryDate = date, Stops =
      [new() { Id = Guid.NewGuid(), TruckId = truck.Id, Sequence = 1, Job = "Pick Up", ScheduledDate = date, Latitude = 40, Longitude = from },
       new() { Id = Guid.NewGuid(), TruckId = truck.Id, Sequence = 2, Job = "Drop Off", ScheduledDate = date, Latitude = 40, Longitude = to }]
    };
    var current = Load(1, today, -81, -79);
    current.Stops[0].PickedUpAt = DateTime.UtcNow.AddHours(-1);
    var future = Load(2, today.AddDays(1), -78, -77);
    db.Dispatches.AddRange(current, future);
    await db.SaveChangesAsync();
    var profile = new TruckRouteProfile { Confirmed = true, TankGallons = 100, Mpg = 5,
      ReserveGallons = 10, FillPercent = 100, StopCostUsd = 20 };
    var baseRoute = Route(-78, -77);
    var persistedFuture = await db.Dispatches.AsNoTracking().Include(load => load.Stops).SingleAsync(load => load.Id == future.Id);
    db.DispatchBaseRoutes.Add(new() { Id = Guid.NewGuid(), DispatchId = future.Id,
      InputHash = BaseRouteService.Signature(persistedFuture, profile), RouteJson = RoutePlanStorage.Serialize(baseRoute),
      CalculatedAt = baseRoute.CalculatedAt });
    var history = await new DeadheadHistoryReader(db).ReadAsync([future.Id], default);
    var pair = DeadheadConnection.Find(history[future.Id])
      ?? throw new InvalidOperationException("The fixture requires a saved-route predecessor.");
    db.DispatchDeadheads.Add(new() { Id = Guid.NewGuid(), DispatchId = future.Id, PreviousDispatchId = current.Id,
      InputHash = pair.Signature(profile), Miles = 100, RouteJson = RoutePlanStorage.Serialize(Route(-79, -78)) });
    await db.SaveChangesAsync();
    var plan = new RoutePlan { TruckId = truck.Id, DispatchId = current.Id, FromCurrentPosition = true,
      Profile = profile, Route = Route(-80, -79),
      Stops = [new(current.Stops[1].Id, "Current delivery", "", 2, new(40, -79))] };
    plan.Tracking.NextStopId = current.Stops[1].Id;
    var state = new RoutePlanningState(profile, plan,
      new(0, 100, 6000, 0, false, false, DateTime.UtcNow, new(40, -80)), 80, DateTime.UtcNow, false);
    var router = new ForbiddenRouter();
    return new() { Connection = connection, Db = db, Current = current, Future = future, State = state,
      Router = router, Services = new(db, router) };
  }

  public static TruckRoute Route(double from, double to) => new()
  {
    Miles = 100, Seconds = 6000, Legs = [new(100, 6000, [new(40, from), new(40, to)])]
  };

  public async ValueTask DisposeAsync()
  {
    Services.Dispose();
    await Db.DisposeAsync();
    await Connection.DisposeAsync();
  }

  internal sealed class ForbiddenRouter : IRoutingProvider
  {
    public bool IsConfigured => false;
    public int Calls { get; private set; }
    public Task<TruckRoute> CalculateAsync(IReadOnlyList<RoutePoint> points, TruckRouteProfile profile, CancellationToken ct)
    { Calls++; throw new InvalidOperationException("Fuel must use saved roads only."); }
    public Task<RoutePoint> GeocodeAsync(string address, CancellationToken ct)
    { Calls++; throw new InvalidOperationException("Fuel must use confirmed saved coordinates only."); }
  }
}
