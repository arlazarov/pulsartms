using System.Text.Json;
using Domain.Models.Routing;
using Domain.Rules;
using Domain.Rules.Routing;

namespace Application.Features.Routing.Services.Routes;

public static partial class RoutePlanStorage
{
  public static async Task<TruckRoute?> ReadRoadAtAsync(
    IAppDbContext db,
    Guid routePlanId,
    long revision,
    CancellationToken ct
  )
  {
    if (
      revision < 1
      || !await db
        .DispatchRoutePlans.AsNoTracking()
        .AnyAsync(x => x.Id == routePlanId, ct)
    )
      return null;
    var rows = db
      .RouteGeometryChanges.AsNoTracking()
      .Where(x => x.RoutePlanId == routePlanId && x.Revision <= revision)
      .OrderBy(x => x.Revision)
      .AsAsyncEnumerable();
    var ranges = new List<List<RouteChunkRange>>();
    List<RouteChunkMeasure> measures = [];
    long expected = 1;
    var calculatedAt = DateTime.MinValue;
    await foreach (var row in rows.WithCancellation(ct))
    {
      if (row.Revision != expected++)
        throw new InvalidOperationException("Incomplete route change history.");
      using var json = JsonDocument.Parse(row.ChangesJson);
      var root = json.RootElement;
      if (root.GetProperty("format").GetInt32() != 1)
        throw new InvalidOperationException("Unsupported route change format.");
      foreach (var element in root.GetProperty("changes").EnumerateArray())
      {
        var splice = element.Deserialize<Splice>(RoutingJson.Options)!;
        if (splice.Road != "route")
          continue;
        while (ranges.Count <= splice.Leg)
          ranges.Add([]);
        var leg = ranges[splice.Leg];
        if (
          splice.At < 0
          || splice.At > leg.Count - splice.Removed.Count
          || !leg.Skip(splice.At)
            .Take(splice.Removed.Count)
            .SequenceEqual(splice.Removed)
        )
          throw new InvalidOperationException("Invalid route change interval.");
        leg.RemoveRange(splice.At, splice.Removed.Count);
        leg.InsertRange(splice.At, splice.Inserted);
      }
      calculatedAt = root.GetProperty("calculatedAt").GetDateTime();
      var count = root.GetProperty("routeLegs").GetInt32();
      while (ranges.Count < count)
        ranges.Add([]);
      if (ranges.Count > count)
        ranges.RemoveRange(count, ranges.Count - count);
      measures = root.GetProperty("measures")
        .Deserialize<List<RouteChunkMeasure>>(RoutingJson.Options)!;
    }
    if (expected != revision + 1)
      return null;
    var keys = ranges
      .SelectMany(x => x)
      .Select(x => x.Key)
      .Distinct()
      .ToArray();
    var chunks = await db
      .RouteGeometryChunks.AsNoTracking()
      .Where(x => x.RoutePlanId == routePlanId && keys.Contains(x.Key))
      .ToListAsync(ct);
    var route = new TruckRoute
    {
      CalculatedAt = calculatedAt,
      Miles = measures.Sum(x => x.Miles),
      Seconds = measures.Sum(x => x.Seconds),
      Legs = measures
        .Select(x => new RouteLeg(x.Miles, x.Seconds, []))
        .ToList(),
    };
    Restore(
      route,
      ranges,
      measures,
      chunks.ToDictionary(
        x => x.Key,
        x => RouteChunkPacker.Decode(x.CoordinatesJson)
      )
    );
    return route;
  }
}
