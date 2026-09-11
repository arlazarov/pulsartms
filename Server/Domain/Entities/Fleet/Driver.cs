namespace Domain.Entities.Fleet;

public class Driver : BaseEntity
{
  public string ExternalId { get; set; } = string.Empty;
  public string Name { get; set; } = string.Empty;
  public string FuelCard { get; set; } = string.Empty;
  public bool IsActive { get; set; }
}
