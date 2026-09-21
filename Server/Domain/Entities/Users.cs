namespace Domain.Entities;

public class User : BaseEntity, ICompanyOwned
{
  public Guid CompanyId { get; set; }

  public string IdentityUserId { get; set; } = string.Empty;
  public string Name { get; set; } = string.Empty;
  public string Email { get; set; } = string.Empty;
  public bool IsActive { get; set; } = true;

  public string Theme { get; set; } = "light";
  public string TemperatureUnit { get; set; } = "both";
  public string DistanceUnit { get; set; } = "both";
}
