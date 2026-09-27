namespace Domain.Entities;

public class User : BaseEntity, ICompanyOwned
{
  public Guid CompanyId { get; set; }

  public string IdentityUserId { get; set; } = string.Empty;
  public string Name { get; set; } = string.Empty;
  public string Email { get; set; } = string.Empty;
  public bool IsActive { get; set; } = true;

  // Null until the user chooses: the Client then applies the product
  // default (dark). A saved choice, light or dark, is kept as chosen.
  public string? Theme { get; set; }
  public string TemperatureUnit { get; set; } = "both";
  public string DistanceUnit { get; set; } = "both";

  // The dispatcher's own driver group that narrows the program's driver
  // lists everywhere, or null for all drivers. Only a filter.
  public Guid? SelectedDriverGroupId { get; set; }
}
