using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using Application.Features.Routing.Models;

namespace Application.Features.Routing.Services.Routes;

public static class RoutePlanStorage
{
  private static readonly JsonSerializerOptions Options = CreateOptions();
  private static JsonSerializerOptions CreateOptions()
  {
    var resolver = new DefaultJsonTypeInfoResolver();
    resolver.Modifiers.Add(info =>
    {
      if (info.Type != typeof(TruckRoute)) return;
      foreach (var property in info.Properties.Where(x => x.Name == "points"))
        property.ShouldSerialize = (_, _) => false;
    });
    return new(RoutePlanningService.Json) { TypeInfoResolver = resolver };
  }
  public static string Serialize(RoutePlan plan) => JsonSerializer.Serialize(plan, Options);
  public static string Serialize(TruckRoute route) => JsonSerializer.Serialize(route, Options);
}
