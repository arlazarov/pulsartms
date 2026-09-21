namespace Domain.Entities.Mileage;

public sealed class OdometerInterval : BaseEntity, ICompanyOwned
{
  public Guid CompanyId { get; set; }

  public Guid TruckId { get; init; }
  public string ExternalTruckId { get; init; } = "";
  public DateTime StartedAt { get; init; }
  public DateTime EndedAt { get; init; }
  public decimal StartMeters { get; init; }
  public decimal EndMeters { get; init; }
  public DateTime RecordedAt { get; init; }
  public DateTime? CheckedAt { get; set; }
  public string Status { get; set; } = "pending";
  public Guid? MovementId { get; set; }
  public Guid? GapId { get; set; }
}
