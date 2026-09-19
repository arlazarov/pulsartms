using Application.Features.Fleet.Models;

namespace Application.Features.Routing.Algorithms;

public static class TruckLocationFreshness
{
  public static bool IsStale(TruckLocation? truck, DateTime now)
  {
    if (truck is null || truck.UpdatedAt > now.AddMinutes(1))
      return true;
    if (truck.UpdatedAt >= now.AddMinutes(-10))
      return false;
    var running =
      truck.EngineState.Equals("idle", StringComparison.OrdinalIgnoreCase)
      || truck.EngineState.Equals("idling", StringComparison.OrdinalIgnoreCase)
      || truck.EngineState.Equals("on", StringComparison.OrdinalIgnoreCase)
      || truck.EngineState.Equals(
        "running",
        StringComparison.OrdinalIgnoreCase
      );
    return truck.Speed >= 1
      || running
      || truck.ObservedAt < now.AddMinutes(-2)
      || truck.UpdatedAt < now.AddHours(-24);
  }
}
