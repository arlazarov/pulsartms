namespace Domain.Models.Routing;

public sealed record FuelStopScheduleImpact(
  Guid DispatchId,
  Guid StopId,
  DateTimeOffset? BaselineArrival,
  DateTimeOffset? CandidateArrival,
  int? BaselineLateMinutes,
  int? CandidateLateMinutes,
  int? AddedLateMinutes,
  int? BaselineCycleAtArrivalMinutes,
  int? CandidateCycleAtArrivalMinutes,
  bool CycleKnown,
  bool BaselineCycleShort,
  bool CycleShort
);

public sealed record FuelScheduleImpact(
  DateTime CalculatedAt,
  bool Complete,
  bool CycleKnown,
  bool BaselineCycleShort,
  bool CycleShort,
  int? AddedMinutes,
  int? AddedLateMinutes,
  IReadOnlyList<FuelStopScheduleImpact> Stops,
  string? UnavailableReason
);
