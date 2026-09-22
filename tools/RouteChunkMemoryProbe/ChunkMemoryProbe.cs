using System.Data;
using System.Diagnostics;
using System.Text.Json;
using Application.Features.Routing.Services.Routes;
using Domain.Entities.Dispatch;
using Domain.Rules.Routing;
using Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;

internal static class ChunkMemoryProbe
{
  public static async Task RunAsync(bool allocations = false)
  {
    var config = new ConfigurationBuilder()
      .AddUserSecrets("pulsartms-api-local")
      .Build();
    await using var db = new AppDbContext(
      new DbContextOptionsBuilder<AppDbContext>()
        .UseNpgsql(config.GetConnectionString("DefaultConnection"))
        .Options
    );
    await using var transaction = await db.Database.BeginTransactionAsync(
      IsolationLevel.RepeatableRead
    );
    await db.Database.ExecuteSqlRawAsync(
      "SET TRANSACTION READ ONLY; SET LOCAL statement_timeout='15s'"
    );
    var trucks = await db
      .Trucks.AsNoTracking()
      .Select(x => new { x.Id, x.UnitNumber })
      .ToListAsync();
    var entities = await db.DispatchRoutePlans.AsNoTracking().ToListAsync();
    foreach (var entity in entities)
      await RoutePlanStorage.LoadAsync(db, entity, CancellationToken.None);
    var metadata = new Dictionary<Guid, string>();
    if (allocations)
    {
      var reader = new SavedRoutePlanReader(
        db,
        NullLogger<SavedRoutePlanReader>.Instance
      );
      foreach (var entity in entities)
      {
        var value = entity.ExecutionLegId is { } legId
          ? await reader.ReadExecutionLegAsync(legId, default)
          : await reader.ReadAsync(entity.DispatchId, default);
        metadata[entity.Id] = JsonSerializer.Serialize(value);
      }
    }
    await transaction.RollbackAsync();

    if (allocations)
    {
      RouteAllocationProbe.Run(entities.ToArray(), metadata);
      return;
    }

    foreach (var group in entities.GroupBy(x => x.TruckId))
    {
      var rows = group.ToArray();
      _ = Measure(rows);
      var samples = Enumerable.Range(0, 5).Select(_ => Measure(rows)).ToArray();
      var plans = rows.Select(RoutePlanStorage.Read).ToArray();
      Console.WriteLine(
        JsonSerializer.Serialize(
          new
          {
            Truck = trucks.Single(x => x.Id == group.Key).UnitNumber,
            Plans = rows.Length,
            Converted = rows.Count(x => x.GeometryManifestJson is not null),
            Points = plans.Sum(x => x!.Route.Legs.Sum(y => y.Points.Count)),
            SourceStringBytes = rows.Sum(x =>
              2L
              * (
                x.PlanJson.Length
                + (x.GeometryManifestJson?.Length ?? 0)
                + x.GeometryChunks.Sum(y => y.CoordinatesJson.Length)
              )
            ),
            Samples = samples,
          }
        )
      );
    }
  }

  private static object Measure(DispatchRoutePlan[] rows)
  {
    const int copies = 8;
    var baseline = Collect();
    var allocated = GC.GetAllocatedBytesForCurrentThread();
    var timer = Stopwatch.StartNew();
    var plans = Enumerable
      .Range(0, copies)
      .SelectMany(_ => rows.Select(x => RoutePlanStorage.Read(x)!))
      .ToArray();
    var indexes = plans.Select(x => new RouteGeometry(x.Route)).ToArray();
    var snapshots = Enumerable
      .Range(0, copies)
      .SelectMany(_ => rows.Select(RouteDisplayCache.Create))
      .ToArray();
    timer.Stop();
    allocated = GC.GetAllocatedBytesForCurrentThread() - allocated;
    var retained = Collect() - baseline;
    var result = new
    {
      Copies = copies,
      RetainedBytes = retained / copies,
      AllocatedBytes = allocated / copies,
      Milliseconds = timer.Elapsed.TotalMilliseconds / copies,
      DisplayCacheEstimatedBytes = snapshots.Sum(x => x.Size) / copies,
      IndexEstimatedBytes = indexes.Sum(x => x.EstimatedBytes) / copies,
    };
    GC.KeepAlive(plans);
    GC.KeepAlive(indexes);
    GC.KeepAlive(snapshots);
    return result;
  }

  private static long Collect()
  {
    GC.Collect(2, GCCollectionMode.Forced, true, true);
    GC.WaitForPendingFinalizers();
    GC.Collect(2, GCCollectionMode.Forced, true, true);
    return GC.GetTotalMemory(false);
  }
}
