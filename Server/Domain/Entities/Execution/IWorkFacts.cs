namespace Domain.Entities.Execution;

// Pure policies read these facts synchronously. Asynchronous calculations
// capture immutable work before awaiting external input.
public interface IWorkFacts
{
  Guid Id { get; }
  Guid? TruckId { get; }
  Guid? ExecutionLegId { get; }
  long AssignmentRevision { get; }
  string? ExecutionStatus { get; }
  string Status { get; }
  long RouteChoiceRevision { get; }
  DateOnly? ShipDate { get; }
  bool AwaitingReceipt { get; }
  IReadOnlyList<IWorkStopFacts> Stops { get; }
}

public interface IWorkStopFacts
{
  Guid Id { get; }
  int Sequence { get; }
  Guid? TruckId { get; }
  string Job { get; }
  string? ManualAction { get; }
  string StateAfter { get; }
  long OperationRevision { get; }
  bool DriverOnly { get; }
  bool IsCompleted { get; }
  bool? CompletionOverride { get; }
  string Address { get; }
  string City { get; }
  string Province { get; }
  string Country { get; }
  string ZipCode { get; }
  decimal? Latitude { get; }
  decimal? Longitude { get; }
  DateOnly? ScheduledDate { get; }
  TimeOnly? ScheduledTime { get; }
  DateOnly? ScheduledDate2 { get; }
  TimeOnly? ScheduledTime2 { get; }
  string AppointmentTimeZoneId { get; }
  DateTime? PickedUpAt { get; }
  DateTime? ManualCompletedAt { get; }
  long ManualCompletionRevision { get; }
}
