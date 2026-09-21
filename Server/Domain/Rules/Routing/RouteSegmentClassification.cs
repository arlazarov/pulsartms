using Domain.Models.Routing;

namespace Domain.Rules.Routing;

public static class RouteSegmentClassification
{
  public static List<RouteSegmentMeaning> Read(RoutePlan plan)
  {
    var result = new List<RouteSegmentMeaning>();
    for (var index = 0; index < plan.Route.Legs.Count; index++)
    {
      var preceding = index - (plan.FromCurrentPosition ? 1 : 0);
      PlanStop? origin = plan.Stops.ElementAtOrDefault(preceding);
      var state = origin?.StateAfter ?? "Unknown";
      if (preceding < 0 && plan.Stops.FirstOrDefault() is { } destination)
      {
        var full = plan.ReferenceStops ?? plan.Stops;
        var position = full.FindIndex(x => x.Id == destination.Id);
        origin = position > 0 ? full[position - 1] : null;
        state =
          origin?.StateAfter
          ?? (
            position == 0
            && destination.Sequence == 1
            && destination.Job is "Pick Up" or "Pickup"
              ? "Empty"
              : "Unknown"
          );
      }
      result.Add(FromState(state));
    }
    return result;
  }

  public static RouteSegmentMeaning FromState(string? state)
  {
    state = state is "Loaded" or "Empty" or "Bobtail" ? state : "Unknown";
    return new(
      state,
      state is "Empty" or "Bobtail" ? "Deadhead"
        : state == "Loaded" ? "Freight"
        : "Unknown"
    );
  }
}
