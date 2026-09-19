namespace Application.Features.Routing.Models;

public sealed class TruckRoute
{
  public DateTime CalculatedAt { get; set; } = DateTime.UtcNow;
  public double Miles { get; set; }
  public double Seconds { get; set; }
  public List<RouteLeg> Legs { get; set; } = [];
  public List<RoutePoint> Points { get; set; } = [];
  public List<string> Warnings { get; set; } = [];

  public bool TryGetLegSeconds(out double seconds)
  {
    seconds = 0;
    if (!double.IsFinite(Seconds) || Seconds < 0 || Legs is not { Count: > 0 })
      return false;
    foreach (var leg in Legs)
    {
      if (leg is null || !double.IsFinite(leg.Seconds) || leg.Seconds < 0)
        return false;
      seconds += leg.Seconds;
    }
    // Whole-route and per-leg provider summaries can round independently to
    // seconds.
    return double.IsFinite(seconds)
      && Math.Abs(Seconds - seconds) <= Legs.Count;
  }
}
