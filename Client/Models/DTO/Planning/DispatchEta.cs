namespace Client.Models.DTO.Planning;

public sealed record StopEta(
  Guid StopId,
  DateTimeOffset Arrival,
  string TimeZoneId,
  DateTimeOffset? Appointment,
  int? LateMinutes,
  int DrivingMinutes,
  int RestMinutes
)
{
  public Guid DispatchId { get; init; }
  public DateTimeOffset? ServiceStart { get; init; }
  public DateTimeOffset? Departure { get; init; }
  public StopCycleForecast? CycleAfterDeparture { get; init; }
  public StopHoursForecast? Hours { get; init; }
  public int PreTripMinutes { get; init; }
  public int FuelMinutes { get; init; }
}

public sealed record DispatchEta(
  DateTime CalculatedAt,
  DateTime ValidUntil,
  IReadOnlyList<StopEta> Stops,
  string? UnavailableReason,
  IReadOnlyList<string> Assumptions
)
{
  public bool Estimated => true;
  public bool RouteUpdatePending { get; init; }
  public DriverDutyStatus? DutyStatus { get; init; }
  public StopCycleForecast? CycleAtCalculation { get; init; }
  public IReadOnlyDictionary<Guid, string> PendingDispatches { get; init; } =
    new Dictionary<Guid, string>();
}
