namespace Application.Features.Routing.Algorithms;

public static class FuelPricingDate
{
  private static readonly TimeZoneInfo BusinessZone =
    TimeZoneInfo.FindSystemTimeZoneById("America/Toronto");

  public static DateOnly FromUtc(DateTime utc) =>
    DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(utc, BusinessZone));
}
