namespace Domain.Rules;

// Which business day a fuel price belongs to. Prices are published on the
// carrier's own day, not on UTC's.
public static class FuelPricingDate
{
  private static readonly TimeZoneInfo BusinessZone =
    TimeZoneInfo.FindSystemTimeZoneById("America/Toronto");

  public static DateOnly FromUtc(DateTime utc) =>
    DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(utc, BusinessZone));
}
