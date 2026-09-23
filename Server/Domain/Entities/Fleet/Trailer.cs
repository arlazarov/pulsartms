namespace Domain.Entities.Fleet;

public class Trailer : BaseEntity, IFleetConfiguration, ICompanyOwned
{
  public Guid CompanyId { get; set; }

  // The identity a source gave it: a telemetry provider's id, or empty for
  // a trailer first seen by number on an imported load. Source names which;
  // null is a row from before sources were recorded, owned by the
  // configured telemetry provider.
  public string ExternalId { get; set; } = string.Empty;
  public string? Source { get; set; }

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
