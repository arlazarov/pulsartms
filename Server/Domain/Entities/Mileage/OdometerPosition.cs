namespace Domain.Entities.Mileage;

public sealed class OdometerPosition : BaseEntity, ICompanyOwned
{
  public Guid CompanyId { get; set; }

  public Guid TruckId { get; init; }
  public string ExternalTruckId { get; set; } = "";
  public DateTime ObservedAt { get; set; }
  public decimal Meters { get; set; }
  public long Revision { get; set; }
}

public sealed class OdometerCaptureCheckpoint : BaseEntity, ICompanyOwned
{
  public Guid CompanyId { get; set; }

  public static readonly Guid SingletonId = Guid.Parse(
    "0d332d2b-f0bf-4e07-97b7-f2805f684219"
  );

  public string? Cursor { get; set; }
  public long Revision { get; set; }
  public DateTime UpdatedAt { get; set; }
}
