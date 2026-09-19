using System.Collections.Immutable;
using System.Text.Json.Serialization;
using Domain.Entities.Dispatch;
using Domain.Entities.Execution;

namespace Application.Features.Routing.Models;

public sealed record RouteWorkSnapshot(
  Guid Id,
  Guid? TruckId,
  string TruckNumber,
  string Status,
  Guid? ExecutionLegId,
  string? ExecutionStatus,
  long AssignmentRevision,
  Guid? PlanningTruckId,
  Guid? PlanningFromStopId,
  long PlanningAssignmentRevision,
  long RouteChoiceRevision,
  DateOnly? ShipDate,
  DateOnly? DeliveryDate,
  decimal? Price,
  string Currency,
  decimal? LoadedMiles,
  ImmutableArray<RouteWorkStop> Stops
) : IWorkFacts
{
  IReadOnlyList<IWorkStopFacts> IWorkFacts.Stops => Stops;
  bool IWorkFacts.AwaitingReceipt =>
    Stops.FirstOrDefault()?.AwaitingHandoff == true;

  public int LoadNumber { get; init; }
  public Guid? DriverId { get; init; }
  public Guid? CoDriverId { get; init; }
  public Guid? TrailerId { get; init; }
}

public sealed record RouteWorkStop(
  Guid Id,
  Guid? TruckId,
  string TruckNumber,
  int Sequence,
  string Job,
  string? ManualAction,
  string? ManualStateAfter,
  string StateAfter,
  long OperationRevision,
  bool AwaitingHandoff,
  bool ExecutionCompleted,
  DateOnly? ScheduledDate,
  TimeOnly? ScheduledTime,
  DateTime? PickedUpAt,
  DateTime? DeliveredAt,
  DateTime? DepartedAt,
  DateTime? ManualCompletedAt,
  bool? CompletionOverride,
  long ManualCompletionRevision,
  string Name,
  string Address,
  string City,
  string Province,
  string Country,
  string ZipCode,
  decimal? Latitude,
  decimal? Longitude,
  DateTime? AddressVerifiedAt,
  DateTime? AddressRetryAfter,
  string SourceAddressJson
) : ITruckPathStop, IWorkStopFacts
{
  public Guid? DriverId { get; init; }
  public Guid? CoDriverId { get; init; }
  public Guid? TrailerId { get; init; }
  public DateTime? ArrivedAt { get; init; }
  public Guid? ManualCompletedBy { get; init; }
  public DateOnly? ScheduledDate2 { get; init; }
  public TimeOnly? ScheduledTime2 { get; init; }
  public bool IsWindow { get; init; }
  public string AppointmentTimeZoneId { get; init; } = "";
  public string Commodity { get; init; } = "";
  public string Notes { get; init; } = "";

  bool IWorkStopFacts.DriverOnly => StateAfter == "No truck";

  [JsonIgnore]
  public bool IsCompleted =>
    StopCompletion.IsCompleted(
      AwaitingHandoff,
      CompletionOverride,
      ExecutionCompleted,
      (DepartedAt ?? DeliveredAt ?? PickedUpAt ?? ManualCompletedAt).HasValue
    );
}
