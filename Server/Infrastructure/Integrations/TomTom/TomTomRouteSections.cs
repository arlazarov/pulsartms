using System.Text.Json;
using Domain.Models.Routing;

namespace Infrastructure.Integrations.TomTom;

internal static class TomTomRouteSections
{
  public static IReadOnlyList<RouteSection> Read(JsonElement sections) =>
    sections
      .EnumerateArray()
      .Select(s => new RouteSection(
        Text(s, "simpleCategory") == "TRUCK_RESTRICTIONS"
          || Text(s, "sectionType") == "TRUCK",
        Text(s, "travelMode") == "other",
        Text(s, "travelMode") == "truck",
        Index(s, "startPointIndex"),
        Index(s, "endPointIndex")
      ))
      .ToArray();

  private static string? Text(JsonElement section, string name) =>
    section.TryGetProperty(name, out var value) ? value.GetString() : null;

  private static int? Index(JsonElement section, string name) =>
    section.TryGetProperty(name, out var value)
    && value.TryGetInt32(out var index)
      ? index
      : null;
}
