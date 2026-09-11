namespace Client.Models.DTO.Planning;

public sealed record DriverDutyStatus(string Status, DateTimeOffset? StatusStartedAt,
  DateTimeOffset? RestStartedAt, DateTimeOffset ObservedAt)
{
  public int? CycleResetHours { get; init; }
  public string? CycleResetCountry { get; init; }
  public int? CycleResetRemainingMinutes { get; init; }
  public int? StatusMinutes => StatusStartedAt is { } start ? (int)Math.Max(0, (ObservedAt - start).TotalMinutes) : null;
  public int? RestMinutes => RestStartedAt is { } start ? (int)Math.Max(0, (ObservedAt - start).TotalMinutes) : null;
  public int? TenHourRestRemainingMinutes => RestMinutes is { } minutes ? Math.Max(0, 600 - minutes) : null;
}
