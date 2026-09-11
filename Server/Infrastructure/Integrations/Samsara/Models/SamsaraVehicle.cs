namespace Infrastructure.Integrations.Samsara.Models;

public class SamsaraVehicle
{
  public string Id { get; set; } = string.Empty;
  public string Name { get; set; } = string.Empty;
  public string Vin { get; set; } = string.Empty;
  public List<SamsaraAttribute> Attributes { get; set; } = [];
}
