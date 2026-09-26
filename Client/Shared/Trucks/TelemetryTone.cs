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
  // reads as a confirmed normal one.
  public static string Speed(decimal? speed) =>
    speed switch
    {
      null => "is-unknown",
      > 70 => "is-critical",
      > 65 => "is-low",
      _ => "is-normal",
    };

  public static string Engine(decimal speed, string? state) =>
    speed >= 1 ? "is-normal"
    : state?.Trim().ToLowerInvariant()
      is "idle"
        or "idling"
        or "on"
        or "running"
      ? "is-low"
    : "is-unknown";
}
