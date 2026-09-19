namespace Infrastructure.Integrations.Samsara.Models;

public class SamsaraHosClock
{
  public HosDriver Driver { get; set; } = new();
  public HosClocks? Clocks { get; set; }
  public DutyStatus? CurrentDutyStatus { get; set; }

  public class DutyStatus
  {
    public string HosStatusType { get; set; } = "";
  }

  public class HosDriver
  {
    public string Id { get; set; } = "";
  }

  public class HosClocks
  {
    public BreakClock? Break { get; set; }
    public DriveClock? Drive { get; set; }
    public ShiftClock? Shift { get; set; }
    public CycleClock? Cycle { get; set; }
  }

  public class BreakClock
  {
    public long? TimeUntilBreakDurationMs { get; set; }
  }

  public class DriveClock
  {
    public long? DriveRemainingDurationMs { get; set; }
  }

  public class ShiftClock
  {
    public long? ShiftRemainingDurationMs { get; set; }
  }

  public class CycleClock
  {
    public long? CycleRemainingDurationMs { get; set; }
  }
}
