using Application.Features.Routing.Models;

namespace Application.Features.Routing.Algorithms;

public sealed record FuelRouteWindow(
  double Entry,
  double Exit,
  double BaselineSeconds
)
{
  // A diversion never crosses a required stop: its anchors stay inside one
  // route leg.
  public static FuelRouteWindow Create(
    TruckRoute route,
    double progress,
    double along,
    double away
  )
  {
    double start = 0;
    foreach (var leg in route.Legs)
    {
      var end = start + leg.Miles;
      if (along <= end && end > progress)
      {
        var radius = Math.Max(15, away * 3);
        var entry = Math.Max(Math.Max(progress, start), along - radius);
        var exit = Math.Min(end, along + radius);
        return new(
          entry,
          exit,
          leg.Miles > 0 ? leg.Seconds * (exit - entry) / leg.Miles : 0
        );
      }
      start = end;
    }
    throw new ArgumentException("Station is outside the remaining route.");
  }
}
