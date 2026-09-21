using System.Text.Json;

namespace Application.Features.Routing.Services.Routes;

// How routing writes JSON, wherever it writes it: saved plans, signatures,
// fingerprints. It lived on RoutePlanningService, and a hundred and twenty
// places reached into the planning service for nothing but this.
public static class RoutingJson
{
  public static readonly JsonSerializerOptions Options = new(
    JsonSerializerDefaults.Web
  );
}
