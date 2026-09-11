namespace Client.Models.DTO.Planning;

public class DriverHosClocks
{
  public long? BreakMs { get; set; }
  public long? DriveMs { get; set; }
  public long? ShiftMs { get; set; }
  public long? CycleMs { get; set; }
  public DateTime UpdatedAt { get; set; }
  public string? CurrentDutyStatus { get; set; }
}
