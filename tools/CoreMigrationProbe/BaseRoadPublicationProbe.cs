using Application.Caching;
using Application.Features.Execution.Models;
using Application.Features.Execution.Services;
using Application.Features.Fuel.Interfaces;
using Application.Features.Fuel.Models;
using Application.Features.Fuel.Services;
using Application.Features.Routing.Algorithms;
using Application.Features.Routing.Exceptions;
using Application.Features.Routing.Interfaces;
using Application.Features.Routing.Models;
using Application.Features.Routing.Services.Deadheads;
using Application.Features.Routing.Services.Routes;
using Application.Features.Synchronization.Options;
using Domain.Entities.Dispatch;
using Domain.Entities.Execution;
using Domain.Entities.Fleet;
using Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Load = Domain.Entities.Dispatch.Dispatch;

namespace CoreMigrationProbe;

internal static class BaseRoadPublicationProbe
{
  public static async Task VerifyAsync(AppDbContext db)
  {
    db.ChangeTracker.Clear();
    using var reads = new ReadCache(
      Options.Create(new SynchronizationOptions())
    );
    var publication = new PlanningPublicationScope(db);
    var readScope = new ExecutionReadScope(db);
    var profiles = new TruckPlanningProfileService(
      db,
      reads,
      new(db, reads),
      new FuelExchangeRateService(
        new FuelExchangeRateStore(db, publication),
        new NoExchangeProvider(),
        reads,
        TimeProvider.System
      )
    );
    var router = new Router();
    var bases = new BaseRouteService(
      db,
      router,
      new(
        new TruckItineraryReader(db, readScope),
        publication,
        new DeadheadHistoryService(db, new DeadheadHistoryReader(db), readScope)
      ),
      profiles,
      publication
    );
    var truck = new Truck
    {
      Id = Guid.NewGuid(),
      ExternalId = "base-publication-fixture",
      UnitNumber = "base-publication-fixture",
      IsActive = true,
    };
    db.Trucks.Add(truck);
    await db.SaveChangesAsync();
    var profile = await profiles.GetAsync(truck.Id, default);
    foreach (var historical in new[] { false, true })
    {
      var load = new Load
      {
        Id = Guid.NewGuid(),
        TruckId = truck.Id,
        Status = "assigned",
        LoadNumber = await db.Dispatches.MaxAsync(x => x.LoadNumber) + 1,
        Stops = new[] { 40m, 43m }
          .Select(
            (latitude, index) =>
              new DispatchStop
              {
                Id = Guid.NewGuid(),
                Sequence = index + 1,
                Latitude = latitude,
                Longitude = -80,
              }
          )
          .ToList(),
      };
      db.Dispatches.Add(load);
      ExecutionLeg? leg = null;
      if (historical)
      {
        leg = new()
        {
          Id = Guid.NewGuid(),
          TruckId = truck.Id,
          Trip = new() { Id = Guid.NewGuid() },
          Status = "completed",
          Revision = 1,
          Stops = ExecutionStopRows.Capture(load.Stops),
          Loads =
          [
            new()
            {
              Id = Guid.NewGuid(),
              DispatchId = load.Id,
              Sequence = 1,
              StartVisitId = load.Stops[0].Id,
              EndVisitId = load.Stops[1].Id,
            },
          ],
        };
        db.ExecutionLegs.Add(leg);
      }
      await db.SaveChangesAsync();
      db.ChangeTracker.Clear();
      var input = leg is null
        ? RouteWorkProjection.Capture(load)
        : RouteWorkProjection.Capture(load, leg, load.Stops);
      await bases.EnsureAsync(input, profile, default);
      await db
        .DispatchBaseRoutes.Where(x => x.DispatchId == load.Id)
        .ExecuteUpdateAsync(s => s.SetProperty(x => x.InputHash, "stale"));
      var before = await db
        .DispatchBaseRoutes.AsNoTracking()
        .SingleAsync(x => x.DispatchId == load.Id);
      var options = new DbContextOptionsBuilder<AppDbContext>()
        .UseNpgsql(db.Database.GetConnectionString())
        .Options;
      router.BeforeCalculate = async () =>
      {
        await using var writer = new AppDbContext(options);
        if (leg is not null)
          await writer
            .ExecutionLegs.Where(x => x.Id == leg.Id)
            .ExecuteUpdateAsync(s =>
              s.SetProperty(x => x.Revision, x => x.Revision + 1)
            );
        else
          await writer
            .DispatchStops.Where(x => x.Id == load.Stops[1].Id)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.Longitude, -79m));
      };
      var rejected = false;
      try
      {
        await bases.EnsureAsync(input, profile, default);
      }
      catch (RoutePlanningException ex)
        when (ex.Message.Contains("base route work changed"))
      {
        rejected = true;
      }
      var retained = await db
        .DispatchBaseRoutes.AsNoTracking()
        .SingleAsync(x => x.DispatchId == load.Id);
      if (
        !rejected
        || retained.RouteJson != before.RouteJson
        || retained.InputHash != before.InputHash
      )
        throw new InvalidOperationException(
          "Stale base publication must retain its saved road."
        );
      router.BeforeCalculate = null;
    }
    Console.WriteLine(
      "PostgreSQL standalone and historical base publication rejected changed work and retained saved roads."
    );
  }

  private sealed class NoExchangeProvider : IFuelExchangeRateProvider
  {
    public Task<FuelExchangeRate> ReadAsync(CancellationToken ct) =>
      throw new InvalidOperationException(
        "Publication must not refresh providers."
      );
  }

  private sealed class Router : IRoutingProvider
  {
    public bool IsConfigured => true;
    public Func<Task>? BeforeCalculate { get; set; }

    public Task<RoutePoint> GeocodeAsync(
      string address,
      CancellationToken ct
    ) => throw new InvalidOperationException("Fixture locations are explicit.");

    public async Task<TruckRoute> CalculateAsync(
      IReadOnlyList<RoutePoint> points,
      TruckRouteProfile profile,
      CancellationToken ct
    )
    {
      if (BeforeCalculate is { } before)
        await before();
      return RouteViaGeometry.Join(
        points
          .Zip(points.Skip(1), (a, b) => new RouteLeg(100, 3600, [a, b]))
          .ToList(),
        [],
        DateTime.UtcNow
      );
    }
  }
}
