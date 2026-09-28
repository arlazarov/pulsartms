namespace Client.Models.DTO.Planning;

public sealed record DriverDutyStatus(
  string Status,
  DateTimeOffset? StatusStartedAt,
  DateTimeOffset? RestStartedAt,
  DateTimeOffset ObservedAt
)
{
  public int? CycleResetHours { get; init; }
  public string? CycleResetCountry { get; init; }
  public int? CycleResetRemainingMinutes { get; init; }

  // "US" or "CA": the ruleset the server read these hours under.
  public string? Jurisdiction { get; init; }
  public int? StatusMinutes =>
    StatusStartedAt is { } start
      ? (int)Math.Max(0, (ObservedAt - start).TotalMinutes)
      : null;
  public int? RestMinutes =>
    RestStartedAt is { } start
      ? (int)Math.Max(0, (ObservedAt - start).TotalMinutes)
      : null;

  // The server's reading of the ongoing rest (DriverDutyStatus on the
  // server owns the daily rest length and the cycle reset): shown, never
  // recomputed here.
  public DateTimeOffset? DailyRestCompleteAt { get; init; }
  public int? DailyRestRemainingMinutes { get; init; }
  public DateTimeOffset? CycleResetCompleteAt { get; init; }
}
