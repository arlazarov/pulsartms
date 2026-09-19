namespace Domain.Entities.Fleet;

public class Driver : BaseEntity, IFleetConfiguration
{
  public string ExternalId { get; set; } = string.Empty;
  public string Name { get; set; } = string.Empty;
  public string FuelCard { get; set; } = string.Empty;
  public bool IsActive { get; set; }
  public string? ImportedName { get; set; }
  public string? ImportedFuelCard { get; set; }
  public bool? ImportedIsActive { get; set; }
  public bool IsLocallyConfigured { get; set; }
  public long ConfigurationRevision { get; set; }
  public DateTime? ConfiguredAt { get; set; }
  public string? ConfiguredBy { get; set; }
}
