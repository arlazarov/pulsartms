using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Application.Features.Routing.Services.Routes;
using Domain.Entities.Dispatch;
using Domain.Models.Routing;
using Domain.Rules;
using Domain.Rules.Routing;
using Server.Tests.Support;
using Xunit.Abstractions;

namespace Server.Tests.Routing;

[Collection("Allocation measurements")]
[Trait("Category", "Routing")]
[Trait("Kind", "Allocation")]
public sealed class RouteFleetAllocationTests(ITestOutputHelper output)
{
  [Theory]
  [InlineData(8001)]
  [InlineData(40001)]
  public void HundredIndependentFourThousandKilometreRoads(int count)
  {
    var roads = Enumerable
      .Range(0, 100)
      .Select(truck => Road(count, truck))
      .ToArray();
    _ = new RouteGeometry(roads[0]).Match(roads[0].Legs[0].Points[0]);
    _ = new ReferenceRouteGeometry(roads[0]).Match(roads[0].Legs[0].Points[0]);
    var memory = GC.GetTotalMemory(true);
    var allocated = GC.GetAllocatedBytesForCurrentThread();
    var timer = Stopwatch.StartNew();
    var old = roads.Select(x => new ReferenceRouteGeometry(x)).ToArray();
    var oldMs = timer.Elapsed.TotalMilliseconds;
    var oldAllocated = GC.GetAllocatedBytesForCurrentThread() - allocated;
    var oldRetained = GC.GetTotalMemory(true) - memory;
    memory = GC.GetTotalMemory(true);
    allocated = GC.GetAllocatedBytesForCurrentThread();
    timer.Restart();
    var current = roads.Select(x => new RouteGeometry(x)).ToArray();
    var newMs = timer.Elapsed.TotalMilliseconds;
    var newAllocated = GC.GetAllocatedBytesForCurrentThread() - allocated;
    var newRetained = GC.GetTotalMemory(true) - memory;
    var oldTimes = new double[500];
    var newTimes = new double[500];
    for (var i = 0; i < 500; i++)
    {
      var truck = i % 100;
      var point = roads[truck].Legs[0].Points[(i * 73) % count];
      var start = Stopwatch.GetTimestamp();
      var expected = old[truck].Match(point);
      oldTimes[i] = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
      start = Stopwatch.GetTimestamp();
      var actual = current[truck].Match(point);
      newTimes[i] = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
      Assert.Equal(expected.Along, actual.Along, 6);
      Assert.Equal(expected.Away, actual.Away, 6);
    }
    Array.Sort(oldTimes);
    Array.Sort(newTimes);
    var plan = new RoutePlan { Route = roads[0], ReferenceRoute = roads[0] };
    var legacy = Bytes(RoutePlanStorage.Serialize(plan));
    var packer = new RouteChunkPacker([]);
    var ranges = roads[0].Legs.Select(x => packer.Pack(x.Points)).ToList();
    var measures = roads[0]
      .Legs.Select(x => new RouteChunkMeasure(x.Miles, x.Seconds))
      .ToList();
    var manifest = new RouteChunkManifest(
      1,
      ranges,
      ranges,
      measures,
      measures
    );
    var stored = new DispatchRoutePlan
    {
      PlanJson = RoutePlanStorage.SerializeState(plan),
      GeometryManifestJson = JsonSerializer.Serialize(
        manifest,
        RoutingJson.Options
      ),
      GeometryChunks = packer
        .Added.Select(x => new RouteGeometryChunk
        {
          Key = x.Key,
          CoordinatesJson = x.Value,
        })
        .ToList(),
    };
    var legacyJson = RoutePlanStorage.Serialize(plan);
    var oldRead = Reads(
      () =>
        JsonSerializer.Deserialize<RoutePlan>(legacyJson, RoutingJson.Options)
    );
    var newRead = Reads(() => RoutePlanStorage.Read(stored));
    var activeBytes =
      Bytes(RoutePlanStorage.SerializeState(plan))
      + Bytes(JsonSerializer.Serialize(manifest, RoutingJson.Options))
      + packer.Added.Values.Sum(Bytes);
    var existing = packer.Added.Select(x => (x.Key, x.Value)).ToArray();
    var detour = roads[0].Legs[0].Points.ToList();
    detour[count / 2] = detour[count / 2] with
    {
      Latitude = detour[count / 2].Latitude + .01,
    };
    var changed = new RouteChunkPacker(existing);
    changed.Pack(detour);
    var detourBytes = changed.Added.Values.Sum(Bytes);
    output.WriteLine(
      JsonSerializer.Serialize(
        new
        {
          Trucks = 100,
          Kilometres = 4000,
          PointsPerTruck = count,
          OldIndexAllocated = oldAllocated,
          NewIndexAllocated = newAllocated,
          OldIndexRetained = oldRetained,
          NewIndexRetained = newRetained,
          OldBuildMs = oldMs,
          NewBuildMs = newMs,
          OldMatchP50Ms = oldTimes[250],
          OldMatchP95Ms = oldTimes[475],
          NewMatchP50Ms = newTimes[250],
          NewMatchP95Ms = newTimes[475],
          LegacyPlanBytes = legacy,
          ActiveChunkPayloadBytes = activeBytes,
          OldTenReadsAllocated = oldRead.Bytes,
          NewTenReadsAllocated = newRead.Bytes,
          OldTenReadsMs = oldRead.Milliseconds,
          NewTenReadsMs = newRead.Milliseconds,
          AddedDetourBytes = detourBytes,
          NewDetourChunks = changed.Added.Count,
        }
      )
    );
    Assert.True(newAllocated < oldAllocated * .65);
    Assert.True(activeBytes < legacy * .5);
    Assert.Single(changed.Added);
    GC.KeepAlive(old);
    GC.KeepAlive(current);
    GC.KeepAlive(roads);
  }

  private static (long Bytes, double Milliseconds) Reads(Func<RoutePlan?> load)
  {
    GC.KeepAlive(load());
    var before = GC.GetAllocatedBytesForCurrentThread();
    var timer = Stopwatch.StartNew();
    for (var i = 0; i < 10; i++)
      GC.KeepAlive(load());
    return (
      GC.GetAllocatedBytesForCurrentThread() - before,
      timer.Elapsed.TotalMilliseconds
    );
  }

  private static int Bytes(string value) => Encoding.UTF8.GetByteCount(value);

  private static TruckRoute Road(int count, int truck)
  {
    var points = Enumerable
      .Range(0, count)
      .Select(i => new RoutePoint(
        42 + truck * .001 + Math.Sin(i * .013) * .01,
        -120 + i * 50d / (count - 1)
      ))
      .ToList();
    const double miles = 4000 / 1.609344;
    return new()
    {
      Miles = miles,
      Seconds = 180000,
      Legs = [new(miles, 180000, points)],
    };
  }
}
