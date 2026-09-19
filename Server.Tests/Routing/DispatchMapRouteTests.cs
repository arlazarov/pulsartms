using System.Text.Json;
using Application.Features.Execution.Models;
using Application.Features.Execution.Services;
using Application.Features.Routing.Background;
using Application.Features.Routing.Models;
using Application.Features.Routing.Options;
using Application.Features.Routing.Queries;
using Application.Features.Routing.Services.Routes;
using Domain.Entities.Dispatch;
using Domain.Entities.Execution;
using Domain.Entities.Fleet;
using Infrastructure.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Load = Domain.Entities.Dispatch.Dispatch;

namespace Server.Tests.Routing;

[Trait("Category", "Routing")]
[Trait("Kind", "Integration")]
public sealed class DispatchMapRouteTests
{
  [Theory]
  [InlineData(false)]
  [InlineData(true)]
  public async Task ReadsSeparateCompletedAndActiveRoadsWithoutProviderCallsAndRejectsStaleGeometry(
    bool stale
  )
  {
    await using var connection = new SqliteConnection("Data Source=:memory:");
    await connection.OpenAsync();
    await using var db = new AppDbContext(
      new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connection).Options
    );
    await db.Database.EnsureCreatedAsync();
    var trucks = new[]
    {
      new Truck
      {
        Id = Guid.NewGuid(),
        UnitNumber = "54777",
        ExternalId = "54777",
      },
      new Truck
      {
        Id = Guid.NewGuid(),
        UnitNumber = "11005",
        ExternalId = "11005",
      },
    };
    db.Trucks.AddRange(trucks);
    var load = new Load { Id = Guid.NewGuid(), Status = "in_transit" };
    load.Stops = Enumerable
      .Range(0, 4)
      .Select(index => new DispatchStop
      {
        Id = Guid.NewGuid(),
        DispatchId = load.Id,
        Sequence = index + 1,
        Latitude = 40 + index,
        Longitude = -75,
        Job = index == 0 ? "Pick Up" : "Drop Off",
      })
      .ToList();
    db.Dispatches.Add(load);
    var legs = Enumerable
      .Range(0, 2)
      .Select(index => new ExecutionLeg
      {
        Id = Guid.NewGuid(),
        Trip = new Trip { Id = Guid.NewGuid() },
        TruckId = trucks[index].Id,
        Status = index == 0 ? "completed" : "active",
        Revision = 1,
        Stops = ExecutionStopRows.Capture(load.Stops.Skip(index * 2).Take(2)),
      })
      .ToArray();
    db.ExecutionLegs.AddRange(legs);
    db.LoadExecutionLegs.AddRange(
      legs.Select(
        (leg, index) =>
          new LoadExecutionLeg
          {
            Id = Guid.NewGuid(),
            DispatchId = load.Id,
            ExecutionLegId = leg.Id,
            Sequence = index + 1,
          }
      )
    );
    await db.SaveChangesAsync();
    using var services = new PlanningTestServices(db);
    var profiles = new TruckPlanningProfileService(
      db,
      services.Reads,
      services.Settings,
      services.ExchangeRates
    );
    foreach (var leg in legs)
    {
      var section = RouteWorkProjection.Capture(
        load,
        leg,
        ExecutionStopRows.Read(leg)
      );
      var points = section
        .Stops.Select(x => new RoutePoint(
          (double)x.Latitude!,
          (double)x.Longitude!
        ))
        .ToList();
      var road = new TruckRoute
      {
        CalculatedAt = DateTime.UtcNow,
        Miles = 100,
        Seconds = 6000,
        Legs = [new(100, 6000, points)],
      };
      db.DispatchBaseRoutes.Add(
        new()
        {
          DispatchId = load.Id,
          ExecutionLegId = leg.Id,
          InputHash =
            stale && leg == legs[1]
              ? "old"
              : BaseRouteService.Signature(
                section,
                await profiles.GetAsync(leg.TruckId, default)
              ),
          RouteJson = JsonSerializer.Serialize(road, RoutePlanningService.Json),
          CalculatedAt = road.CalculatedAt,
        }
      );
    }
    await db.SaveChangesAsync();
    db.ChangeTracker.Clear();
    var preparation = new SourceRoadDemand(
      new SourceRoadStore(db),
      TimeProvider.System
    );
    var result = (
      await new GetDispatchMapRouteHandler(
        db,
        services.Routes,
        profiles,
        preparation
      ).Handle(new(load.Id), default)
    ).Response!;
    Assert.Equal(stale ? 1 : 2, result.Segments.Count);
    Assert.Equal(stale ? 1 : 0, result.MissingSections);
    Assert.Equal(load.Stops[0].Id, result.Segments[0].FromStopId);
    Assert.Equal(load.Stops[1].Id, result.Segments[0].ToStopId);
    Assert.DoesNotContain(
      result.Segments,
      x => x.FromStopId == load.Stops[1].Id
    );
    Assert.False(db.ChangeTracker.HasChanges());
    Assert.Equal(
      stale ? 1 : 0,
      await db.SourceRoadRequests.CountAsync(x =>
        x.RequestedVersion > x.CompletedVersion
      )
    );
  }
}
