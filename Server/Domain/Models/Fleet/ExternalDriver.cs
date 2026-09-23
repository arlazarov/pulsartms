namespace Domain.Models.Fleet;

public class ExternalDriver
{
  public string ExternalId { get; set; } = string.Empty;
  public string Name { get; set; } = string.Empty;
  public string FuelCard { get; set; } = string.Empty;
  public bool IsActive { get; set; }
  public string? Phone { get; set; }
  public string? Email { get; set; }
}
