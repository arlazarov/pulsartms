namespace Domain.Entities.Fleet;

// The last hours reading observed for one driver, kept so an instance that
// does not run the refresh can still answer, and so a restart does not start
// blind. The provider remains the source; this is its latest observation.
public sealed class DriverHosReading
{
  public string DriverExternalId { get; set; } = "";
  public long? BreakMs { get; set; }
  public long? DriveMs { get; set; }
  public long? ShiftMs { get; set; }
  public long? CycleMs { get; set; }
  public string? CurrentDutyStatus { get; set; }

  // When the provider observed it, not when this row was written.
  public DateTime ObservedAt { get; set; }
  public DateTime RecordedAt { get; set; }
}
