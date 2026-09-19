using Client.Models.DTO.Planning;

namespace Client.Models.DTO.Dispatch;

public sealed record DispatchStopRevision(
  Guid Id,
  long ManualCompletionRevision,
  long OperationRevision
);

public sealed record DispatchFinancialValues(
  decimal? EmptyMiles,
  decimal? TotalMiles,
  decimal? LoadedRatePerMile,
  decimal? TotalRatePerMile,
  string EmptyMilesStatus
);

public sealed record DispatchEnrichment(
  Guid Id,
  Guid? TruckId,
  DateTime LastSyncedAt,
  long PlanningAssignmentRevision,
  long RouteChoiceRevision,
  IReadOnlyList<DispatchStopRevision> Stops,
  DispatchFinancialValues? Financials,
  DispatchEta? Eta,
  Guid? ExecutionLegId = null,
  long AssignmentRevision = 0
);

public sealed record TruckDispatchEnrichment(
  string Key,
  Guid? TruckId,
  string DriverName,
  DriverCycleSnapshot? CurrentCycle,
  IReadOnlyList<DispatchEnrichment> Dispatches
);
