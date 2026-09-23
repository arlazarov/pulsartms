namespace Domain.Entities.Fleet;

public class Driver : BaseEntity, IFleetConfiguration, ICompanyOwned
{
  public Guid CompanyId { get; set; }

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

  // Contact details are owned field by field: each follows the source until
  // somebody here sets or clears it, and a later import only refreshes the
  // source copy. The WhatsApp number has no source - an ordinary phone is
  // never assumed to be registered on WhatsApp.
  public string? Phone { get; set; }
  public string? ImportedPhone { get; set; }
  public bool PhoneIsLocal { get; set; }
  public string? Email { get; set; }
  public string? ImportedEmail { get; set; }
  public bool EmailIsLocal { get; set; }
  public string? WhatsAppPhone { get; set; }
  public long ContactRevision { get; set; }
  public DateTime? ContactChangedAt { get; set; }
  public string? ContactChangedBy { get; set; }
}
