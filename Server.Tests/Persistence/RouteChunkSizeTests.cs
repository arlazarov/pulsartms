using System.Text;
using System.Text.Json;
using Application.Features.Fleet.Queries.GetFleetLocations;
using Application.Features.Routing.Services.Routes;
using Domain.Entities.Dispatch;
using Domain.Entities.Fleet;
using Domain.Models.Routing;
using Microsoft.EntityFrameworkCore;
using Xunit.Abstractions;
using DispatchEntity = Domain.Entities.Dispatch.Dispatch;

namespace Server.Tests.Persistence;

[Trait("Category", "Database")]
[Trait("Kind", "Integration")]
public sealed class RouteChunkSizeTests(ITestOutputHelper output)
{
  [RequiresPostgresFact]
  public async Task CompareStoredPayloadAndProgressWritesOnTheIsolatedFixture()
  {
    await using var fixture = await PostgresFixture.CreateAsync();
    var db = fixture.Connect();
    await db.Database.OpenConnectionAsync();
    await db.Database.ExecuteSqlRawAsync(
      "CREATE TEMP TABLE route_chunk_legacy (payload text NOT NULL)"
    );
    var truck = new Truck { Id = Guid.NewGuid() };
    var load = new DispatchEntity { Id = Guid.NewGuid(), TruckId = truck.Id };
    var points = Enumerable
      .Range(0, 8001)
      .Select(i => new RoutePoint(
        42 + Math.Sin(i * .013) * .01,
        -120 + i * 50d / 8000
      ))
      .ToList();
    var route = new TruckRoute
    {
      Miles = 4000 / 1.609344,
      Seconds = 180000,
      Legs = [new(4000 / 1.609344, 180000, points)],
    };
    var plan = new RoutePlan
    {
      Id = Guid.NewGuid(),
      DispatchId = load.Id,
      TruckId = truck.Id,
      Route = route,
      ReferenceRoute = route,
    };
    var entity = new DispatchRoutePlan
    {
      Id = plan.Id,
      DispatchId = load.Id,
      TruckId = truck.Id,
      CreatedAt = DateTime.UtcNow,
    };
    db.Trucks.Add(truck);
    db.Dispatches.Add(load);
    db.DispatchRoutePlans.Add(entity);
    var legacy = RoutePlanStorage.Serialize(plan);
    await db.Database.ExecuteSqlInterpolatedAsync(
      $"INSERT INTO route_chunk_legacy(payload) VALUES ({legacy})"
    );
    await RoutePlanStorage.PrepareAsync(db, entity, plan, default);
    await db.SaveChangesAsync();
    var beforeChunks = await db.RouteGeometryChunks.CountAsync();
    var history = await new GetTruckMovementHandler(db).Handle(
      new(truck.Id, DateTimeOffset.UtcNow.AddHours(-1), DateTimeOffset.UtcNow),
      default
    );
    Assert.True(history.Success);
    Assert.Empty(history.Response!.Segments);
    var savedBytes = await Scalar(
      """
      SELECT (
        (SELECT coalesce(sum(pg_column_size("CoordinatesJson")), 0)
          FROM "RouteGeometryChunks")
        + (SELECT coalesce(sum(pg_column_size("PlanJson")
          + pg_column_size("GeometryManifestJson")), 0)
          FROM "DispatchRoutePlans")
        + (SELECT coalesce(sum(pg_column_size("ChangesJson")), 0)
          FROM "RouteGeometryChanges")
      )::bigint AS "Value"
      """
    );
    var legacyBytes = await Scalar(
      "SELECT pg_column_size(payload)::bigint AS \"Value\" FROM route_chunk_legacy"
    );
    long stateWrites = 0;
    long legacyWrites = 0;
    for (var i = 0; i < 20; i++)
    {
      plan.Tracking.LastObservationAt = DateTime.UtcNow.AddSeconds(i);
      legacyWrites += Encoding.UTF8.GetByteCount(
        RoutePlanStorage.Serialize(plan)
      );
      await RoutePlanStorage.PrepareAsync(db, entity, plan, default);
      stateWrites += Encoding.UTF8.GetByteCount(entity.PlanJson);
      await db.SaveChangesAsync();
    }
    Assert.Equal(beforeChunks, await db.RouteGeometryChunks.CountAsync());
    Assert.Single(await db.RouteGeometryChanges.ToListAsync());
    Assert.True(stateWrites < legacyWrites / 100);
    output.WriteLine(
      JsonSerializer.Serialize(
        new
        {
          Kilometres = 4000,
          Points = 8001,
          LegacyColumnBytes = legacyBytes,
          ChunkStateAndChangeColumnBytes = savedBytes,
          ProgressUpdates = 20,
          LegacyWritePayloadBytes = legacyWrites,
          ChunkWritePayloadBytes = stateWrites,
          AddedChunksDuringProgress = 0,
        }
      )
    );
    return;

    Task<long> Scalar(string sql) =>
      db.Database.SqlQueryRaw<long>(sql).SingleAsync();
  }
}
