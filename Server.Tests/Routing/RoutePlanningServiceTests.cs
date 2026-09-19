using Application.Features.Fleet.Models;
using Application.Features.Routing.Algorithms;
using Application.Features.Routing.Exceptions;
using Application.Features.Routing.Interfaces;
using Application.Features.Routing.Models;
using Application.Features.Routing.Services;
using Application.Features.Routing.Services.Routes;
using Application.Models;
using Domain.Entities.Dispatch;
using Domain.Entities.Fleet;
using Infrastructure.Persistence;
using MediatR;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Server.Tests.Routing;

using Dispatch = global::Domain.Entities.Dispatch.Dispatch;

[Trait("Category", "Routing")]
public class RoutePlanningServiceTests
{
  [Fact]
  public async Task HeaderTruckCannotRouteStopsAssignedToAnotherTruck()
  {
    await using var connection = new SqliteConnection("Data Source=:memory:");
    await connection.OpenAsync();
    await using var db = new AppDbContext(
      new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connection).Options
    );
    await db.Database.EnsureCreatedAsync();
    var truck = new Truck
    {
      Id = Guid.NewGuid(),
      ExternalId = "header",
      UnitNumber = "header",
    };
    var other = new Truck
    {
      Id = Guid.NewGuid(),
      ExternalId = "other",
      UnitNumber = "other",
    };
    var load = new Dispatch
    {
      Id = Guid.NewGuid(),
      Truck = truck,
      Stops =
      [
        new()
        {
          Id = Guid.NewGuid(),
          Sequence = 1,
          Truck = truck,
          Latitude = 40,
          Longitude = -80,
        },
        new()
        {
          Id = Guid.NewGuid(),
          Sequence = 2,
          Truck = other,
          Latitude = 41,
          Longitude = -79,
        },
      ],
    };
    db.Dispatches.Add(load);
    await db.SaveChangesAsync();
    var router = new FakeRouter();
    using var services = new PlanningTestServices(db, router);
    await Assert.ThrowsAsync<RoutePlanningException>(
      () =>
        services.Routes.BuildAsync(
          load.Id,
          new(new() { Confirmed = true }),
          default
        )
    );
    Assert.Equal(0, router.Calls);
    Assert.Empty(await db.DispatchRoutePlans.ToListAsync());
  }

  [Fact]
  public void StopAssignmentChangesInvalidateSavedRouteInputs()
  {
    var truck = Guid.NewGuid();
    var stop = new DispatchStop
    {
      Id = Guid.NewGuid(),
      TruckId = truck,
      Sequence = 1,
    };
    var load = new Dispatch { TruckId = truck, Stops = [stop] };
    var previous = RoutePlanningService.HashInputs(load, new());
    stop.TruckId = Guid.NewGuid();
    Assert.NotEqual(previous, RoutePlanningService.HashInputs(load, new()));
  }

  [Fact]
  public async Task SavedRouteSurvivesReloadAndGpsProgressDoesNotCallRouter()
  {
    await using var connection = new SqliteConnection("Data Source=:memory:");
    await connection.OpenAsync();
    await using var db = new AppDbContext(
      new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connection).Options
    );
    await db.Database.EnsureCreatedAsync();
    var truck = new Truck { Id = Guid.NewGuid(), ExternalId = "test-truck" };
    var load = new Dispatch
    {
      Id = Guid.NewGuid(),
      LoadNumber = 456,
      Status = "assigned",
      Truck = truck,
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
          Latitude = 40,
          Longitude = -79,
        },
      ],
    };
    db.Dispatches.Add(load);
    await db.SaveChangesAsync();
    var telemetry = new TruckLocation
    {
      TruckId = truck.Id,
      Latitude = 40,
      Longitude = -79.5m,
      UpdatedAt = DateTime.UtcNow,
      FuelPercent = 60,
      FuelUpdatedAt = DateTime.UtcNow,
    };
    var provider = new FakeRouter();
    var sender = new TelemetrySender(telemetry);
    using var services = new PlanningTestServices(db, provider, sender);
    var service = services.Routes;
    var profile = new TruckRouteProfile { Confirmed = true };
    var first = await service.BuildAsync(load.Id, new(profile), default);
    var repeat = await service.BuildAsync(load.Id, new(profile), default);
    Assert.Equal(first.Version, repeat.Version);
    Assert.Equal(1, provider.Calls);
    db.ChangeTracker.Clear();
    var state = await service.GetAsync(load.Id, default);
    Assert.Equal(100, state.Plan!.OriginalPlannedMiles);
    Assert.Equal(50, state.Progress!.RemainingMiles!.Value, 3);
    Assert.Equal(50, state.Progress.ProgressMiles!.Value, 3);
    Assert.Equal(1, provider.Calls);
    telemetry.Latitude = 42;
    var offRoute = await service.GetAsync(load.Id, default);
    Assert.True(offRoute.Progress!.OffRoute);
    Assert.NotNull(offRoute.Progress.RemainingMiles);
    telemetry.Latitude = 40;
    telemetry.UpdatedAt = DateTime.UtcNow.AddHours(-1);
    var stale = await service.GetAsync(load.Id, default);
    Assert.True(stale.Progress!.LocationStale);
    Assert.Null(stale.Progress.RemainingMiles);
    telemetry.EngineState = "off";
    telemetry.Speed = 0;
    telemetry.ObservedAt = DateTime.UtcNow;
    var parked = await service.GetAsync(load.Id, default);
    Assert.False(parked.Progress!.LocationStale);
    Assert.Equal(50, parked.Progress.RemainingMiles!.Value, 3);
    var stop = await db.DispatchStops.SingleAsync(x =>
      x.Id == load.Stops[1].Id
    );
    stop.Longitude = -78;
    await db.SaveChangesAsync();
    services.Reads.Invalidate("dispatch");
    Assert.True((await service.GetAsync(load.Id, default)).Plan!.InputsChanged);
    Assert.Equal(1, provider.Calls);
  }

  [Fact]
  public async Task PlannedDisplayReadKeepsFuelTelemetryWithoutGpsProgress()
  {
    await using var connection = new SqliteConnection("Data Source=:memory:");
    await connection.OpenAsync();
    await using var db = new AppDbContext(
      new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connection).Options
    );
    await db.Database.EnsureCreatedAsync();
    var truck = new Truck { Id = Guid.NewGuid(), ExternalId = "planned-truck" };
    var load = new Dispatch
    {
      Id = Guid.NewGuid(),
      Truck = truck,
      ExecutionStatus = "planned",
      Status = "assigned",
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
    var observedAt = DateTime.UtcNow;
    var telemetry = new TruckLocation
    {
      TruckId = truck.Id,
      Latitude = 40,
      Longitude = -80,
      UpdatedAt = observedAt,
      FuelPercent = 52,
      FuelUpdatedAt = observedAt,
    };
    using var services = new PlanningTestServices(
      db,
      new FakeRouter(),
      new TelemetrySender(telemetry)
    );
    await services.Routes.BuildAsync(
      load.Id,
      new(new() { Confirmed = true }),
      default
    );

    var state = await services.Routes.GetAsync(
      load,
      default,
      cachedTelemetryOnly: true
    );

    Assert.Equal(52, state.FuelPercent);
    Assert.Equal(observedAt, state.FuelUpdatedAt);
    Assert.Null(state.Progress);
  }

  [Fact]
  public void SavingsCompareCompletePlansWithStopAndTimeCosts()
  {
    var p = new TruckRouteProfile
    {
      Confirmed = true,
      TankGallons = 100,
      Mpg = 5,
      ReserveGallons = 10,
      FillPercent = 100,
      StopCostUsd = 20,
    };
    FuelCandidate Candidate(string name, double mile, double price) =>
      new(
        new FuelPlanStop { Name = name, StationId = Guid.NewGuid() },
        mile,
        0,
        0,
        price,
        price
      );
    var plan = FuelOptimizer.Optimize(
      350,
      30,
      p,
      [Candidate("A", 80, 4.5), Candidate("B", 170, 2.5)],
      1,
      false
    );
    Assert.Equal(2, plan.Stops.Count);
    Assert.Equal(new[] { 25d, 25d }, plan.Stops.Select(x => x.BuyGallons));
    Assert.Equal(30d, plan.SavingsUsd);
  }

  private sealed class FakeRouter : IRoutingProvider
  {
    public bool IsConfigured => true;
    public int Calls;

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
          Miles = 100,
          Seconds = 7200,
          Points = points.ToList(),
          Legs = [new(100, 7200, points.ToList())],
        }
      );
    }

    public Task<RoutePoint> GeocodeAsync(
      string address,
      CancellationToken ct
    ) => throw new InvalidOperationException("Coordinates already exist.");
  }

  private sealed class TelemetrySender(TruckLocation truck) : ISender
  {
    public Task<TResponse> Send<TResponse>(
      IRequest<TResponse> request,
      CancellationToken ct = default
    ) =>
      Task.FromResult(
        (TResponse)
          (object)
            RequestResponse<FleetLocationsResponse>.Ok(
              new() { Trucks = [truck] }
            )
      );

    public Task Send<TRequest>(TRequest request, CancellationToken ct = default)
      where TRequest : IRequest => throw new NotSupportedException();

    public Task<object?> Send(object request, CancellationToken ct = default) =>
      throw new NotSupportedException();

    public IAsyncEnumerable<TResponse> CreateStream<TResponse>(
      IStreamRequest<TResponse> request,
      CancellationToken ct = default
    ) => throw new NotSupportedException();

    public IAsyncEnumerable<object?> CreateStream(
      object request,
      CancellationToken ct = default
    ) => throw new NotSupportedException();
  }
}
