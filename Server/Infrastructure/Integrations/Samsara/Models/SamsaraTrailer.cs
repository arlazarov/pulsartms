namespace Infrastructure.Integrations.Samsara.Models;

public class SamsaraTrailer
{
  public string Id { get; set; } = string.Empty;
  public string Name { get; set; } = string.Empty;
  public string Vin { get; set; } = string.Empty;
  public bool EnabledForMobile { get; set; }
  public List<SamsaraTag> Tags { get; set; } = [];
}
