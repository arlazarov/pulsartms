namespace Domain.Entities.Fleet;

public class Truck : BaseEntity, IFleetConfiguration, ICompanyOwned
{
  public Guid CompanyId { get; set; }

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
  public Guid? DriverId { get; set; }
  public Driver? Driver { get; set; }

  // The trailer on the truck now, resolved from the evidence below. Every
  // reader of the truck's trailer - the map, the board row, fleet links -
  // reads this one value.
  public Guid? TrailerId { get; set; }
  public Trailer? Trailer { get; set; }

  // Where TrailerId came from: TruckTrailerSources. Null when no source
  // named a trailer.
  public string? TrailerSource { get; set; }

  // A trailer another source names for this truck and that was not taken:
  // the two sources disagree, or another truck holds it.
  public Guid? TrailerConflictId { get; set; }
  public Trailer? TrailerConflict { get; set; }

  // What the telemetry provider last said with certainty. Known with no
  // trailer is an explicit detach; not known is not a detach.
  public bool TelemetryTrailerKnown { get; set; }
  public Guid? TelemetryTrailerId { get; set; }
}
