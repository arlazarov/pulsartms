using Application.Features.Eta.Models;

namespace Application.Features.Dispatch.Models;

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
)
{
  public static TruckDispatchEnrichment From(
    TruckDispatchBoardResponse row,
    bool financials
  ) =>
    new(
      row.Key,
      row.TruckId,
      row.DriverName,
      financials ? null : row.CurrentCycle,
      row.Dispatches.Select(load => new DispatchEnrichment(
          load.Id,
          load.TruckId,
          load.LastSyncedAt,
          load.PlanningAssignmentRevision,
          load.RouteChoiceRevision,
          load.Stops.Select(s => new DispatchStopRevision(
              s.Id,
              s.ManualCompletionRevision,
              s.OperationRevision
            ))
            .ToArray(),
          financials
            ? new(
              load.EmptyMiles,
              load.TotalMiles,
              load.LoadedRatePerMile,
              load.TotalRatePerMile,
              load.EmptyMilesStatus
            )
            : null,
          financials ? null : load.Eta,
          load.ExecutionLegId,
          load.AssignmentRevision
        ))
        .ToArray()
    );
}
