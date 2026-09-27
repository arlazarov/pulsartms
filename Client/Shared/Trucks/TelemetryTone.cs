namespace Client.Shared.Trucks;

public static class TelemetryTone
{
  public static string Fuel(double? percent) =>
    percent switch
    {
      null => "is-unknown",
      <= 15 => "is-critical",
      <= 30 => "is-low",
      _ => "is-normal",
    };

  // Null is a speed nobody knows - no report, or a stale one - and never
  // reads as a confirmed normal one. The band is for a truck whose engine
  // runs, even at 0 mph; a truck with its engine off, or standing with no
  // word of its engine, keeps the quiet icon (the owner, September 27).
  public static string Speed(decimal? speed, string? engine) =>
    speed switch
    {
      null => "is-unknown",
      _ when !Running(speed.Value, engine) => "is-stopped",
      > 70 => "is-critical",
      > 65 => "is-low",
      _ => "is-normal",
    };

  private const decimal Moving = 1;

  private static bool Running(decimal speed, string? engine) =>
    speed >= Moving || IsOn(engine);

  private static bool IsOn(string? engine) =>
    engine?.Trim().ToLowerInvariant()
      is "idle"
        or "idling"
        or "on"
        or "running";

  public static string Engine(decimal speed, string? state) =>
    speed >= Moving ? "is-normal"
    : IsOn(state) ? "is-low"
    : "is-unknown";
}
