namespace Domain.Entities.Fuel;

// The values the place provider reports for whether a business still exists.
// Kept as constants rather than an enum because the value is stored exactly
// as the provider gave it: an unrecognised one has to survive a round trip
// rather than collapse into a default that would read as "open".
public static class FuelStationStatus
{
  public const string Operational = "OPERATIONAL";
  public const string ClosedTemporarily = "CLOSED_TEMPORARILY";
  public const string ClosedPermanently = "CLOSED_PERMANENTLY";

  // Empty means nobody has asked. That is not the same as open, and the
  // difference matters wherever a decision is made about a driver.
  public static bool Unknown(string? status) =>
    string.IsNullOrWhiteSpace(status);

  public static bool Closed(string? status) =>
    status is ClosedTemporarily or ClosedPermanently;
}
