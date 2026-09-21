namespace Domain.Models.Eta;

public sealed record StopHoursForecast(
  int? CycleAtArrivalMinutes,
  int? CycleAfterStopMinutes,
  int? DrivingShortfallMinutes,
  DateTimeOffset? FirstCycleShortageAt,
  bool CycleVerified,
  IReadOnlyList<StopHoursAlternative> Alternatives,
  string? UnavailableReason,
  int? CurrentCycleMinutes = null
);

public sealed record StopHoursAlternative(
  string Kind,
  DateTimeOffset Arrival,
  DateTimeOffset Departure,
  int? LateMinutes,
  int CycleAfterStopMinutes,
  DateTimeOffset? RestStartedAt,
  DateTimeOffset? ResumeAt
);
