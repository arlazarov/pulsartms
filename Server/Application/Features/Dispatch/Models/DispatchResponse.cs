using Application.Features.Eta.Models;
using Domain.Entities.Execution;
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

  // Whether the load is done - decided here, once, and sent. The browser
  // used to work this out for itself from its copy of the stops.
  public bool Completed
  {
    get
    {
      var final = Stops.OrderBy(stop => stop.Sequence).LastOrDefault();
      return LoadCompletion.IsCompleted(
        Status,
        final?.Job,
        final?.CompletionOverride,
        (final?.DeliveredAt ?? final?.DepartedAt).HasValue,
        final?.ManualCompletedAt.HasValue == true,
        Stops.Where(stop => !stop.DriverOnly).All(stop => stop.IsCompleted)
      );
    }
  }

  internal DispatchResponse CopyForBoardRow()
  {
    // Conflicting assignments can place one load in multiple rows; each row
    // owns its forecast.
    var copy = (DispatchResponse)MemberwiseClone();
    copy.Eta = null;
    return copy;
  }
}
