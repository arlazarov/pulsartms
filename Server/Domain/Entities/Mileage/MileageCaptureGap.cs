namespace Domain.Entities.Mileage;

public sealed class MileageCaptureGap : BaseEntity, ICompanyOwned
{
  public Guid CompanyId { get; set; }

  public Guid TruckId { get; init; }
  public Guid? ExecutionLegId { get; init; }
  public DateTime StartedAt { get; init; }
  public DateTime EndedAt { get; init; }
  public string Reason { get; init; } = "";
  public DateTime RecordedAt { get; init; }
}
