using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Domain.Models.Routing;

namespace Domain.Rules.Routing;

public sealed class RouteChunkPacker
{
  public const int MaximumPoints = 128;
  private readonly Dictionary<string, RoutePoint[]> chunks = new();
  private readonly Dictionary<
    RoutePoint,
    List<(string Key, int Index)>
  > starts = new();
  public Dictionary<string, string> Added { get; } = new();

  public RouteChunkPacker(IEnumerable<(string Key, string Json)> existing)
  {
    foreach (var (key, json) in existing)
      AddIndex(key, Decode(json));
  }

  public List<RouteChunkRange> Pack(IReadOnlyList<RoutePoint> points)
  {
    var result = new List<RouteChunkRange>();
    var at = 0;
    while (at < points.Count)
    {
      var reused = Find(points, at);
      if (reused is not null)
      {
        result.Add(reused);
        at += reused.Count;
        continue;
      }
      var end = at + 1;
      while (end < points.Count && end - at < MaximumPoints)
      {
        if (Find(points, end) is not null)
          break;
        end++;
      }
      var coordinates = points.Skip(at).Take(end - at).ToArray();
      var values = new double[coordinates.Length * 2];
      for (var i = 0; i < coordinates.Length; i++)
      {
        values[i * 2] = coordinates[i].Latitude;
        values[i * 2 + 1] = coordinates[i].Longitude;
      }
      var json = JsonSerializer.Serialize(values);
      var key = Convert.ToHexString(
        SHA256.HashData(Encoding.UTF8.GetBytes(json))
      );
      if (!chunks.ContainsKey(key))
      {
        Added.Add(key, json);
        AddIndex(key, coordinates);
      }
      result.Add(new(key, 0, coordinates.Length));
      at = end;
    }
    return result;
  }

  private RouteChunkRange? Find(IReadOnlyList<RoutePoint> points, int at)
  {
    if (!starts.TryGetValue(points[at], out var candidates))
      return null;
    RouteChunkRange? best = null;
    foreach (var (key, index) in candidates)
    {
      var chunk = chunks[key];
      var count = 0;
      while (
        index + count < chunk.Length
        && at + count < points.Count
        && chunk[index + count] == points[at + count]
      )
        count++;
      // A single point is useful only at an endpoint, not as a fragmented
      // substitute for a new road. Coordinates are storage identity only.
      if (
        count > (best?.Count ?? 1)
        || count == 1 && at + count == points.Count
      )
        best = new(key, index, count);
    }
    return best;
  }

  private void AddIndex(string key, RoutePoint[] points)
  {
    if (!chunks.TryAdd(key, points))
      return;
    for (var i = 0; i < points.Length; i++)
    {
      if (!starts.TryGetValue(points[i], out var positions))
        starts.Add(points[i], positions = []);
      // Bound work on repeated vertices and looping roads. A missed reuse
      // opportunity changes storage size, never geometry or route selection.
      if (positions.Count < 16)
        positions.Add((key, i));
    }
  }

  public static RoutePoint[] Decode(string json)
  {
    var values =
      JsonSerializer.Deserialize<double[]>(json)
      ?? throw new InvalidOperationException("Missing route coordinates.");
    if (values.Length % 2 != 0 || values.Length > MaximumPoints * 2)
      throw new InvalidOperationException("Invalid route chunk.");
    var points = new RoutePoint[values.Length / 2];
    for (var i = 0; i < points.Length; i++)
      points[i] = new(values[i * 2], values[i * 2 + 1]);
    return points;
  }
}
