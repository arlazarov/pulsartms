namespace Domain.Models.Eta;

public sealed record DriverDutyStatus(
  string Status,
  DateTimeOffset? StatusStartedAt,
  DateTimeOffset? RestStartedAt,
  DateTimeOffset ObservedAt
)
{
  public int? CycleResetHours { get; init; }
  public string? CycleResetCountry { get; init; }

  // The ruleset the forecast reads these hours under: "US" or "CA" from the
  // truck's current region, null when that region is unknown.
  public string? Jurisdiction { get; init; }
  public int? CycleResetRemainingMinutes =>
    CycleResetHours is { } hours && RestMinutes is { } minutes
      ? Math.Max(0, hours * 60 - minutes)
      : null;
  public int? StatusMinutes =>
    StatusStartedAt is { } start
      ? (int)Math.Max(0, (ObservedAt - start).TotalMinutes)
      : null;
  public int? RestMinutes =>
    RestStartedAt is { } start
      ? (int)Math.Max(0, (ObservedAt - start).TotalMinutes)
      : null;
  public int? TenHourRestRemainingMinutes =>
    RestMinutes is { } minutes ? Math.Max(0, 600 - minutes) : null;
}
