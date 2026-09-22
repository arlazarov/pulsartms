using System.Diagnostics;
using System.Text.Json;
using Application.Features.Routing.Services.Routes;
using Domain.Entities.Dispatch;
using Domain.Models.Routing;
using Domain.Rules;
using Domain.Rules.Routing;

internal static class RouteAllocationProbe
{
  public static void Run(
    DispatchRoutePlan[] rows,
    IReadOnlyDictionary<Guid, string> metadata
  )
  {
    foreach (var row in rows)
    {
      var plan = RoutePlanStorage.Read(row)!;
      var snapshot = RouteDisplayCache.Create(row);
      Measure("decode", () => RoutePlanStorage.Read(row));
      Measure(
        "currency-metadata",
        () =>
          JsonSerializer.Deserialize<SavedRoutePlanMetadata>(metadata[row.Id])
      );
      Measure("index", () => new RouteGeometry(plan.Route));
      Measure("display-cold", () => RouteDisplayCache.Create(row));
      Measure("display-warm", () => snapshot.ReadPlan());
      Measure("metadata-warm", () => snapshot.ReadMetadata());
      Measure("state-write", () => RoutePlanStorage.SerializeState(plan));

      void Measure<T>(string stage, Func<T> action)
      {
        for (var i = 0; i < 3; i++)
          GC.KeepAlive(action());
        const int iterations = 20;
        var timer = new Stopwatch();
        var before = GC.GetAllocatedBytesForCurrentThread();
        timer.Start();
        for (var i = 0; i < iterations; i++)
          GC.KeepAlive(action());
        timer.Stop();
        var bytes = GC.GetAllocatedBytesForCurrentThread() - before;
        Console.WriteLine(
          JsonSerializer.Serialize(
            new
            {
              stage,
              chunked = row.GeometryManifestJson is not null,
              points = plan.Route.Legs.Sum(x => x.Points.Count),
              referencePoints = plan.ReferenceRoute?.Legs.Sum(x =>
                x.Points.Count
              ) ?? 0,
              stateCharacters = row.PlanJson.Length,
              iterations,
              allocatedBytes = bytes / iterations,
              milliseconds = timer.Elapsed.TotalMilliseconds / iterations,
            }
          )
        );
      }
    }
  }
}
