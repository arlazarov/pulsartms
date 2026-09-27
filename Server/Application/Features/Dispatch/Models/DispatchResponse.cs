using System.Text.Json.Serialization;
using Domain.Entities.Execution;
using Domain.Models.Eta;
using Domain.Rules;

namespace Application.Features.Dispatch.Models;

public class DispatchResponse : IWorkFacts
{
  IReadOnlyList<IWorkStopFacts> IWorkFacts.Stops => Stops;

  public DispatchEta? Eta { get; set; }
  public Guid Id { get; set; }
  public Guid? ExecutionLegId { get; set; }
  public long AssignmentRevision { get; set; }
  public string? ExecutionStatus { get; set; }
  public bool AwaitingReceipt { get; set; }
  public Guid? DriverId { get; set; }
  public Guid? TruckId { get; set; }
  public Guid? PlanningTruckId { get; set; }
  public Guid? PlanningFromStopId { get; set; }
  public long PlanningAssignmentRevision { get; set; }
  public long RouteChoiceRevision { get; set; }
  public DateTime? PlanningAssignmentRecordedAt { get; set; }
  public int LoadNumber { get; set; }
  public string OrderNumber { get; set; } = string.Empty;
  public string Status { get; set; } = string.Empty;
  public DateOnly? OrderDate { get; set; }
  public DateOnly? InvoiceDate { get; set; }
  public DateOnly? ShipDate { get; set; }
  public DateOnly? DeliveryDate { get; set; }
  public string CustomerName { get; set; } = string.Empty;
  public string DriverName { get; set; } = string.Empty;
  public string TruckNumber { get; set; } = string.Empty;
  public string TrailerNumber { get; set; } = string.Empty;
  public decimal? LoadedMiles { get; set; }
  public decimal? EmptyMiles { get; set; }
  public decimal? TotalMiles => LoadedMiles + EmptyMiles;
  public decimal? LoadedRatePerMile { get; set; }
  public decimal? TotalRatePerMile { get; set; }
  public string EmptyMilesStatus { get; set; } = "unavailable";
  public decimal? Price { get; set; }
  public string Currency { get; set; } = string.Empty;
  public DateTime LastSyncedAt { get; set; }
  public List<DispatchStopResponse> Stops { get; set; } = [];

  // Where this load stands for its truck, from the planning inputs
  // (TruckPlanningInputs.Placements): current, next, upcoming, earlier or
  // unplaced; stale when the row was read at another assignment revision
  // than the inputs, unknown when the inputs do not hold it, null on a row
  // without a truck. Whether it is done is Completed.
  public string? WorkPhase { get; set; }

  // A disagreement a dispatcher must see and nothing resolves silently:
  // route_passed_not_delivered - planning has passed the load's route, but
  // accepted execution has not completed it.
  public string? WorkConflict { get; set; }

  // Whether the load is done - decided here, once, and sent. In accepted
  // execution: all its legs completed (ExecutionFinished, when a reader of
  // source rows set it). Otherwise closed, or its cargo delivered and the
  // truck's work on it finished (LoadCompletion).
  public bool Completed =>
    ExecutionFinished ?? LoadCompletion.IsCompleted(Status, Facts());

  // Null unless a reader of the load's source rows found it in accepted
  // execution; then whether all its legs not cancelled are completed.
  // Not sent.
  [JsonIgnore]
  public bool? ExecutionFinished { get; set; }

  // Whether its cargo was delivered, which may come before the truck's
  // work on it is finished - a trailer still to drop (CargoDelivery).
  public bool CargoDelivered =>
    ExecutionFinished == true
    || LoadCompletion.IsClosed(Status)
    || Facts().CargoDelivered;

  // Read from the stops as they are now, in one pass, each time: nothing
  // is kept that a later change to the stops could leave stale.
  private CompletionFacts Facts() =>
    WorkCompletion.Of(
      Stops.Select(stop => new CompletionStop(
        stop.Sequence,
        stop.Job,
        stop.DriverOnly,
        stop.CompletionOverride,
        stop.DeliveredAt is not null || stop.DepartedAt is not null,
        stop.ManualCompletedAt is not null,
        stop.IsCompleted
      ))
    );

  internal DispatchResponse CopyForBoardRow()
  {
    // Conflicting assignments can place one load in multiple rows; each row
    // owns its forecast.
    var copy = (DispatchResponse)MemberwiseClone();
    copy.Eta = null;
    return copy;
  }
}
