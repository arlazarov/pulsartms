namespace Domain.Entities.Fuel;

// One hand-over of one planned fuel visit to its driver.
//
// A row is written when somebody confirms the plan was passed on - a
// dispatcher today, a transport later - never because a plan was prepared,
// previewed or copied. It outlives every recalculation, so what the driver
// was told is still known after the plan moves on, and a later plan that
// says something else can be shown as changed since it was sent. Sent is
// not delivered; a delivery or read receipt would be its own record.
public sealed class FuelVisitSend : BaseEntity, ICompanyOwned
{
  public Guid CompanyId { get; set; }
  public Guid TruckId { get; set; }

  // The work the visit belongs to, as accepted when it was sent.
  public Guid DispatchId { get; set; }
  public Guid? ExecutionLegId { get; set; }

  // The leg when there is one and the load when there is not: a key with
  // no missing part, so a repeated confirmation cannot become two rows.
  public Guid ScopeId { get; set; }
  public long AssignmentRevision { get; set; }

  // The visit: this station before this stop.
  public Guid StationId { get; set; }
  public Guid BeforeStopId { get; set; }

  // What was said about it - "full", or the gallons - and the words.
  public string Content { get; set; } = "";
  public string Text { get; set; } = "";
  public DateTime PlanCalculatedAt { get; set; }

  public string Channel { get; set; } = "";
  public DateTime SentAt { get; set; }
  public string? SentBy { get; set; }
}
