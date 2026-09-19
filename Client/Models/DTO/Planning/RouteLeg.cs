using System.Text.Json.Serialization;

namespace Client.Models.DTO.Planning;

public sealed record RouteLeg(
  double Miles,
  double Seconds,
  List<RoutePoint> Points
)
{
  // The same geometry as one encoded string. The server sends this instead
  // of Points, and it is handed to the map as it came: decoding tens of
  // thousands of points into objects here, only to serialize them again for
  // the map, was what froze the page when a truck was selected.
  [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
  public string? Path { get; init; }

  [JsonIgnore]
  public bool HasGeometry => Points.Count > 1 || Path is { Length: > 0 };
}
