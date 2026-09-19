namespace Domain.Entities.Execution;

public sealed class ExecutionLegStop
{
  public Guid ExecutionLegId { get; set; }
  public int Position { get; set; }
  public Guid Id { get; set; }
  public Guid DispatchId { get; set; }
  public Guid? SourceDispatchStopId { get; set; }
  public string Job { get; set; } = "";
  public string StateAfter { get; set; } = "Unknown";
  public long OperationRevision { get; set; }
  public DateTime? OperationRecordedAt { get; set; }
  public Guid? OperationRecordedBy { get; set; }
  public string Name { get; set; } = "";
  public string Address { get; set; } = "";
  public string City { get; set; } = "";
  public string Province { get; set; } = "";
  public string Country { get; set; } = "";
  public string ZipCode { get; set; } = "";
  public decimal? Latitude { get; set; }
  public decimal? Longitude { get; set; }
  public string SourceAddressJson { get; set; } = "";
  public DateTime? AddressVerifiedAt { get; set; }
  public DateTime? AddressRetryAfter { get; set; }
  public DateOnly? ScheduledDate { get; set; }
  public TimeOnly? ScheduledTime { get; set; }
  public DateOnly? ScheduledDate2 { get; set; }
  public TimeOnly? ScheduledTime2 { get; set; }
  public bool IsWindow { get; set; }
  public string AppointmentTimeZoneId { get; set; } = "";
  public bool HasDriverOverride { get; set; }
  public Guid? DriverId { get; set; }
  public Guid? CoDriverId { get; set; }
  public string DriverName { get; set; } = "";
  public string CoDriverName { get; set; } = "";
  public string TrailerNumber { get; set; } = "";
  public string CarrierName { get; set; } = "";
  public string StopNo { get; set; } = "";
  public string Notes { get; set; } = "";
  public string Commodity { get; set; } = "";
  public decimal? Weight { get; set; }
  public string WeightUnit { get; set; } = "";
  public decimal? Pieces { get; set; }
  public decimal? Pallets { get; set; }
  public string Temperature { get; set; } = "";
  public string TemperatureUnit { get; set; } = "";
  public DateTime? ArrivedAt { get; set; }
  public DateTime? PickedUpAt { get; set; }
  public DateTime? DeliveredAt { get; set; }
  public DateTime? DepartedAt { get; set; }
  public DateTime? ManualCompletedAt { get; set; }
  public bool? CompletionOverride { get; set; }
  public bool ExecutionCompleted { get; set; }
  public DateTime? ManualCompletionRecordedAt { get; set; }
  public long ManualCompletionRevision { get; set; }
  public Guid? ManualCompletedBy { get; set; }
  public string? ManualCompletedByName { get; set; }
}
