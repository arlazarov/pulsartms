using System.ComponentModel.DataAnnotations.Schema;
using Domain.Entities.Execution;
using Domain.Entities.Fleet;

namespace Domain.Entities.Dispatch;

public class Dispatch : BaseEntity, IWorkFacts, ICompanyOwned
{
  public Guid CompanyId { get; set; }

  public int LoadNumber { get; set; }
  public string OrderNumber { get; set; } = string.Empty;
  public string Status { get; set; } = string.Empty;
  public DateOnly? OrderDate { get; set; }
  public DateOnly? InvoiceDate { get; set; }
  public DateOnly? ShipDate { get; set; }
  public DateOnly? DeliveryDate { get; set; }
  public Guid? CustomerId { get; set; }
  public Customer? Customer { get; set; }
  public string CustomerName { get; set; } = string.Empty;
  public Guid? DriverId { get; set; }
  public Driver? Driver { get; set; }
  public string DriverName { get; set; } = string.Empty;
  public string CarrierName { get; set; } = string.Empty;
  public Guid? TruckId { get; set; }
  public Truck? Truck { get; set; }
  public string TruckNumber { get; set; } = string.Empty;
  public Guid? TrailerId { get; set; }
  public Trailer? Trailer { get; set; }
  public string TrailerNumber { get; set; } = string.Empty;
  public decimal? LoadedMiles { get; set; }
  public decimal? Price { get; set; }
  public string Currency { get; set; } = string.Empty;
  public DateTime LastSyncedAt { get; set; }
  public List<DispatchStop> Stops { get; set; } = [];
  public Guid? PlanningTruckId { get; set; }
  public Truck? PlanningTruck { get; set; }
  public Guid? PlanningFromStopId { get; set; }
  public DateTime? PlanningAssignmentRecordedAt { get; set; }
  public Guid? PlanningAssignmentRecordedBy { get; set; }
  public long PlanningAssignmentRevision { get; set; }
  public long RouteChoiceRevision { get; set; }

  [NotMapped]
  public Guid? ExecutionLegId { get; set; }

  [NotMapped]
  public long AssignmentRevision { get; set; }

  [NotMapped]
  public string? ExecutionStatus { get; set; }

  IReadOnlyList<IWorkStopFacts> IWorkFacts.Stops => Stops;
  bool IWorkFacts.AwaitingReceipt =>
    Stops.FirstOrDefault()?.AwaitingHandoff == true;

  public Dispatch TruckItinerary()
  {
    if (ExecutionLegId.HasValue)
      return this;
    var copy = (Dispatch)MemberwiseClone();
    var path = TruckPath.Resolve(
      TruckId,
      TruckNumber,
      PlanningTruckId,
      PlanningFromStopId,
      Stops,
      (stop, job, state) => stop.WithOperation(job, state)
    );
    copy.TruckId = path.TruckId;
    copy.TruckNumber = path.TruckNumber;
    copy.Stops = path.Stops.ToList();
    return copy;
  }
}
