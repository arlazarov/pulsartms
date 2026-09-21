using System.Text.Json.Serialization;

namespace Client.Models.DTO.Dispatch;

public class DispatchStopResponse
{
  public bool DriverOnly { get; set; }
  public bool ExecutionCompleted { get; set; }
  public Guid Id { get; set; }
  public Guid? TruckId { get; set; }
  public int Sequence { get; set; }
  public string Job { get; set; } = string.Empty;
  public string? ImportedJob { get; set; }
  public string? ManualAction { get; set; }
  public string? ManualStateAfter { get; set; }
  public string StateAfter { get; set; } = "Unknown";
  public long OperationRevision { get; set; }
  public DateTime? OperationRecordedAt { get; set; }
  public string Name { get; set; } = string.Empty;
  public string Address { get; set; } = string.Empty;
  public string City { get; set; } = string.Empty;
  public string Province { get; set; } = string.Empty;
  public string Country { get; set; } = string.Empty;
  public string ZipCode { get; set; } = string.Empty;
  public decimal? Latitude { get; set; }
  public decimal? Longitude { get; set; }
  public string DriverName { get; set; } = string.Empty;
  public string CoDriverName { get; set; } = string.Empty;
  public string TruckNumber { get; set; } = string.Empty;
  public string TrailerNumber { get; set; } = string.Empty;
  public string Commodity { get; set; } = string.Empty;
  public string Notes { get; set; } = string.Empty;
  public string StopNo { get; set; } = string.Empty;
  public decimal? Weight { get; set; }
  public string WeightUnit { get; set; } = string.Empty;
  public decimal? Pieces { get; set; }
  public decimal? Pallets { get; set; }
  public string Temperature { get; set; } = string.Empty;
  public string TemperatureUnit { get; set; } = string.Empty;
  public DateOnly? ScheduledDate { get; set; }
  public TimeOnly? ScheduledTime { get; set; }
  public DateOnly? ScheduledDate2 { get; set; }
  public TimeOnly? ScheduledTime2 { get; set; }
  public bool IsWindow { get; set; }
  public string AppointmentTimeZoneId { get; set; } = "";
  public DateTime? ArrivedAt { get; set; }
  public DateTime? PickedUpAt { get; set; }
  public DateTime? DeliveredAt { get; set; }
  public DateTime? DepartedAt { get; set; }
  public DateTime? ManualCompletedAt { get; set; }
  public bool? CompletionOverride { get; set; }
  public Guid? ManualCompletedBy { get; set; }
  public string? ManualCompletedByName { get; set; }
  public DateTime? ManualCompletionRecordedAt { get; set; }
  public long ManualCompletionRevision { get; set; }
  public string CompletionIdentity { get; set; } = "";

  // Said by the server. This used to be worked out here from the facts
  // above, by a copy of the server's rule that had already drifted from it:
  // it did not know a stop can be waiting for a handoff.
  public bool IsCompleted { get; set; }
}
