namespace Domain.Entities.Fleet;

public class Trailer : BaseEntity, IFleetConfiguration
{
  public string ExternalId { get; set; } = string.Empty;

  public string UnitNumber { get; set; } = string.Empty;

  public string Vin { get; set; } = string.Empty;

  public bool IsActive { get; set; }
  public string? ImportedVin { get; set; }
  public bool? ImportedIsActive { get; set; }
  public bool IsLocallyConfigured { get; set; }
  public long ConfigurationRevision { get; set; }
  public DateTime? ConfiguredAt { get; set; }
  public string? ConfiguredBy { get; set; }
}
