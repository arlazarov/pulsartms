namespace Client.Models.DTO.Planning;

public sealed record PlanStop(
  Guid Id,
  string Name,
  string Address,
  int Sequence,
  RoutePoint Point
)
{
  public string DisplayAddress =>
    string.Join(
      ", ",
      Address
        .Split(
          ',',
          StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries
        )
        .Distinct(StringComparer.OrdinalIgnoreCase)
    );
  public string Job { get; init; } = "";
  public string StateAfter { get; init; } = "Unknown";
  public DateOnly? ScheduledDate { get; init; }
  public TimeOnly? ScheduledTime { get; init; }
  public DateOnly? ScheduledDate2 { get; init; }

  // The zone the scheduled date and time are written in, when the source
  // says; the server sends it with the plan's stops.
  public string AppointmentTimeZoneId { get; init; } = "";
  public TimeOnly? ScheduledTime2 { get; init; }
  public string Commodity { get; init; } = "";
  public string Notes { get; init; } = "";
}
