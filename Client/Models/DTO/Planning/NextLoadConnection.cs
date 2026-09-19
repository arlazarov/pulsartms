using System.Text.Json.Serialization;

namespace Client.Models.DTO.Planning;

public sealed record NextLoadConnection(
  double Miles,
  IReadOnlyList<RoutePoint> Points
)
{
  // See RouteLeg.Path: the same geometry, handed to the map undecoded.
  [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
  public string? Path { get; init; }
}
