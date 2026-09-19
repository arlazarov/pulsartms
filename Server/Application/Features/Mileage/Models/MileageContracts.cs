namespace Application.Features.Mileage.Models;

public sealed record MileagePolicyState(
  long Revision,
  string YardReturn,
  string Home,
  string Maintenance,
  string Reposition,
  DateTime? UpdatedAt
);

public sealed record MileagePolicyUpdate(
  long Revision,
  string YardReturn,
  string Home,
  string Maintenance,
  string Reposition
);

public sealed record MileageTotals(
  decimal? LoadedMiles,
  decimal? EmptyMiles,
  decimal? BobtailMiles,
  decimal? UnknownMiles,
  decimal? TotalMiles,
  int MissingEvidence
);

public sealed record MileageMovementRow(
  Guid? MovementId,
  long Revision,
  Guid? TruckId,
  Guid? DriverId,
  Guid? CoDriverId,
  Guid? TrailerId,
  string Purpose,
  string CargoState,
  Guid? PreviousDispatchId,
  Guid? NextDispatchId,
  Guid? CarriedDispatchId,
  Guid? AllocatedDispatchId,
  string AllocationTarget,
  string AllocationReason,
  long PolicyRevision,
  bool ManualOverride,
  string Source,
  decimal? PlannedMiles,
  decimal? ActualMiles,
  DateTime? PlannedAt,
  DateTime? ActualAt,
  bool Editable
)
{
  public int? PreviousLoadNumber { get; init; }
  public int? NextLoadNumber { get; init; }
  public int? CarriedLoadNumber { get; init; }
  public int? AllocatedLoadNumber { get; init; }
  public string FromLocation { get; init; } = "";
  public string ToLocation { get; init; } = "";
  public string? TruckNumber { get; init; }
  public string? DriverName { get; init; }
  public string? CoDriverName { get; init; }
  public string? TrailerNumber { get; init; }
  public string? PlannedSource { get; init; }
  public string? PlannedSourceReference { get; init; }
  public string? ActualSource { get; init; }
  public string? ActualSourceReference { get; init; }
  public DateTime? StartedAt { get; init; }
  public DateTime? EndedAt { get; init; }
  public string Origin { get; init; } = "manual";
  public bool CanEditDistance { get; init; } = true;
}

public sealed record DispatchMileageBreakdown(
  Guid DispatchId,
  int LoadNumber,
  MileageTotals Planned,
  MileageTotals Actual,
  IReadOnlyList<MileageMovementRow> Movements,
  bool Truncated
)
{
  public bool ActualIsPartial { get; init; }
  public IReadOnlyList<MileageGapRow> CaptureGaps { get; init; } = [];
  public int PendingObservedIntervals { get; init; }
}

public sealed record MileageGapRow(
  Guid Id,
  Guid TruckId,
  DateTime StartedAt,
  DateTime EndedAt,
  string Reason
);

public sealed record MileageAllocationUpdate(
  long Revision,
  string Target,
  string Reason
);

public sealed record MovementDistanceUpdate(
  long Revision,
  string Basis,
  decimal Miles,
  string Source,
  string SourceReference,
  DateTimeOffset ObservedAt,
  string Reason,
  DateTimeOffset? StartedAt = null,
  DateTimeOffset? EndedAt = null
);

public sealed record RecordMovementRequest(
  Guid IdempotencyKey,
  Guid TruckId,
  Guid? DriverId,
  Guid? CoDriverId,
  Guid? TrailerId,
  Guid? ExecutionLegId,
  string Purpose,
  string CargoState,
  Guid? PreviousDispatchId,
  Guid? NextDispatchId,
  Guid? CarriedDispatchId,
  string FromLocation,
  string ToLocation,
  DateTimeOffset? StartedAt,
  DateTimeOffset? EndedAt
);

public sealed record UnallocatedMileagePage(
  IReadOnlyList<MileageMovementRow> Movements,
  bool HasMore
);
