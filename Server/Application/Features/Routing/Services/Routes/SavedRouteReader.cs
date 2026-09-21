using System.Text.Json;
using Application.Features.Routing.Algorithms;
using Application.Features.Routing.Models;

namespace Application.Features.Routing.Services.Routes;

public static class SavedRouteReader
{
  public static TruckRoute? Route(string? json, int legCount)
  {
    var route = Read<TruckRoute>(json);
    return SavedRouteGeometry.Complete(route, legCount) ? route : null;
  }

  public static RoutePlan? Plan(string? json) => Read<RoutePlan>(json);

  private static T? Read<T>(string? json)
  {
    if (string.IsNullOrWhiteSpace(json))
      return default;
    try
    {
      return JsonSerializer.Deserialize<T>(json, RoutingJson.Options);
    }
    catch (JsonException)
    {
      return default;
    }
  }
}
