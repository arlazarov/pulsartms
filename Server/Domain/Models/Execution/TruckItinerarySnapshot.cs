using System.Collections.Immutable;

namespace Domain.Models.Execution;

public enum WorkReadProblem
{
  MissingVisits,
  UnresolvedTruckPath,
  ConflictingAssignment,
  SourceReviewRequired,
  MissingTransfer,
}

public sealed record WorkVisitFacts(
  Guid Id,
  Guid? SourceStopId,
  int Sequence,
  string Operation,
  string StateAfter,
  string? ManualAction,
  string? ManualStateAfter,
  bool InTruckPath,
  string Commodity,
  string Notes,
  WorkVisitLocation Location,
  WorkAppointment Appointment,
  WorkVisitActuals Actuals,
  Guid? TruckId,
  Guid? DriverId,
  Guid? CoDriverId,
  Guid? TrailerId,
  long OperationRevision
);

public sealed record WorkVisitLocation(
  string Name,
  string Address,
  string City,
  string Province,
  string Country,
  string ZipCode,
  decimal? Latitude,
  decimal? Longitude,
  DateTime? VerifiedAt,
  DateTime? RetryAfter,
  WorkSourceAddress? Source
);

public sealed record WorkSourceAddress(
  string Address,
  string City,
  string Province,
  string Country,
  string ZipCode
);

public sealed record WorkAppointment(
  DateOnly? Date,
  TimeOnly? Time,
  DateOnly? EndDate,
  TimeOnly? EndTime,
  bool IsWindow,
  string TimeZoneId
);

public sealed record WorkVisitActuals(
  DateTime? ArrivedAt,
  DateTime? PickedUpAt,
  DateTime? DeliveredAt,
  DateTime? DepartedAt,
  DateTime? ConfirmedAt,
  Guid? ConfirmedBy,
  bool ExecutionConfirmed,
  bool? CompletionOverride,
  long CompletionRevision,
  bool AwaitingHandoff,
  bool IsCompleted
);

public sealed record TruckWorkSegment(
  WorkIdentity Work,
  string Status,
  int LoadNumber,
  WorkOrderKey DisplayOrder,
  Guid? AssignedTruckId,
  Guid? DriverId,
  Guid? CoDriverId,
  Guid? TrailerId,
  long AssignmentRevision,
  long RouteChoiceRevision,
  DateOnly? ShipDate,
  DateOnly? DeliveryDate,
  bool IsOverdue,
  string? AcceptedSourceSignature,
  string? ObservedSourceSignature,
  string? SourceReviewReason,
  ImmutableArray<WorkVisitFacts> Visits,
  ImmutableArray<WorkReadProblem> Problems
);

public sealed record TruckWorkResources(
  string TruckNumber,
  bool IsActive,
  Guid? DriverId,
  Guid? TrailerId,
  long ConfigurationRevision
);

public sealed record TruckItinerarySnapshot(
  Guid TruckId,
  DateTimeOffset AsOf,
  string InputSignature,
  TruckWorkResources Resources,
  ImmutableArray<TruckWorkSegment> Segments,
  WorkSequenceEvidence Evidence,
  WorkSequenceAssessment Sequence
);
