using Application.Features.Routing.Services.Routes;
using Domain.Entities.Dispatch;
using Domain.Entities.Fleet;
using Domain.Models.Routing;
using Infrastructure.Persistence.Migrations;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;
using DispatchEntity = Domain.Entities.Dispatch.Dispatch;

namespace Server.Tests.Persistence;

[Trait("Category", "Database")]
[Trait("Kind", "Integration")]
public sealed class RouteChunkMigrationTests
{
  [RequiresPostgresFact]
  public async Task UpgradeKeepsLegacyRoadAndDowngradeCannotDiscardChunks()
  {
    await using var f = await PostgresFixture.CreateAsync();
    var db = f.Connect();
    await db.Database.OpenConnectionAsync();
    var truck = new Truck { Id = Guid.NewGuid() };
    var load = new DispatchEntity { Id = Guid.NewGuid(), TruckId = truck.Id };
    var points = new List<RoutePoint> { new(40, -80), new(41, -79) };
    var plan = new RoutePlan
    {
      Id = Guid.NewGuid(),
      DispatchId = load.Id,
      TruckId = truck.Id,
      Route = new()
      {
        Miles = 100,
        Seconds = 6000,
        Legs = [new(100, 6000, points)],
      },
    };
    var entity = new DispatchRoutePlan
    {
      Id = plan.Id,
      DispatchId = load.Id,
      TruckId = truck.Id,
      CreatedAt = DateTime.UtcNow,
      PlanJson = RoutePlanStorage.Serialize(plan),
    };
    db.Trucks.Add(truck);
    db.Dispatches.Add(load);
    db.DispatchRoutePlans.Add(entity);
    await db.SaveChangesAsync();
    var legacy = entity.PlanJson;
    db.ChangeTracker.Clear();
    await db.Database.ExecuteSqlRawAsync(
      """
      DROP TABLE "RouteGeometryChunks";
      DROP TABLE "RouteGeometryChanges";
      ALTER TABLE "DispatchRoutePlans" DROP COLUMN "GeometryManifestJson";
      ALTER TABLE "DispatchRoutePlans" DROP COLUMN "GeometryRevision";
      """
    );
    var migration = new StoreRouteChunks();
    var generator = db.GetService<IMigrationsSqlGenerator>();
    await using (var transaction = await db.Database.BeginTransactionAsync())
    {
      foreach (var command in generator.Generate(migration.UpOperations))
        await db.Database.ExecuteSqlRawAsync(command.CommandText);
      await transaction.CommitAsync();
    }
    entity = await db.DispatchRoutePlans.SingleAsync();
    Assert.Equal(legacy, entity.PlanJson);
    Assert.Null(entity.GeometryManifestJson);
    await RoutePlanStorage.PrepareAsync(
      db,
      entity,
      RoutePlanStorage.Read(entity)!,
      default
    );
    await db.SaveChangesAsync();
    db.ChangeTracker.Clear();
    entity = (
      await RoutePlanStorage.LoadAsync(
        db,
        await db.DispatchRoutePlans.SingleAsync(),
        default
      )
    )!;
    Assert.Equal(points, RoutePlanStorage.Read(entity)!.Route.Legs[0].Points);
    await using (var transaction = await db.Database.BeginTransactionAsync())
    {
      var error = await Assert.ThrowsAsync<PostgresException>(async () =>
      {
        foreach (var command in generator.Generate(migration.DownOperations))
          await db.Database.ExecuteSqlRawAsync(command.CommandText);
      });
      Assert.Equal("P0001", error.SqlState);
      await transaction.RollbackAsync();
    }
    Assert.Single(await db.RouteGeometryChunks.ToListAsync());
    Assert.Single(await db.RouteGeometryChanges.ToListAsync());
    await db.Database.ExecuteSqlRawAsync("DROP TABLE \"RouteMovementChunks\"");
    var movementMigration = new RecordRouteMovement();
    foreach (var command in generator.Generate(movementMigration.UpOperations))
      await db.Database.ExecuteSqlRawAsync(command.CommandText);
    db.RouteMovementChunks.Add(
      new()
      {
        Id = Guid.NewGuid(),
        RoutePlanId = entity.Id,
        TruckId = truck.Id,
        From = DateTime.UtcNow,
        To = DateTime.UtcNow,
      }
    );
    await db.SaveChangesAsync();
    await using (var transaction = await db.Database.BeginTransactionAsync())
    {
      var error = await Assert.ThrowsAsync<PostgresException>(async () =>
      {
        foreach (
          var command in generator.Generate(movementMigration.DownOperations)
        )
          await db.Database.ExecuteSqlRawAsync(command.CommandText);
      });
      Assert.Equal("P0001", error.SqlState);
      await transaction.RollbackAsync();
    }
    Assert.Single(await db.RouteMovementChunks.ToListAsync());
  }
}
