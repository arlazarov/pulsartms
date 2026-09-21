using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using Domain.Models.Routing;
using Domain.Rules;

namespace Application.Features.Routing.Services.Routes;

public static class RoutePlanStorage
{
  private static readonly JsonSerializerOptions Options = CreateOptions();

  private static JsonSerializerOptions CreateOptions()
  {
    var resolver = new DefaultJsonTypeInfoResolver();
    resolver.Modifiers.Add(info =>
    {
      if (info.Type != typeof(TruckRoute))
        return;
      foreach (var property in info.Properties.Where(x => x.Name == "points"))
        property.ShouldSerialize = (_, _) => false;
    });
    return new(RoutingJson.Options) { TypeInfoResolver = resolver };
  }

  public static string Serialize(RoutePlan plan) =>
    JsonSerializer.Serialize(plan, Options);

  public static string Serialize(TruckRoute route) =>
    JsonSerializer.Serialize(route, Options);

  public static string Serialize(SavedRouteChoice choice) =>
    JsonSerializer.Serialize(choice, Options);

  public static string Serialize(RouteChoiceDraft draft) =>
    JsonSerializer.Serialize(draft, Options);
}
