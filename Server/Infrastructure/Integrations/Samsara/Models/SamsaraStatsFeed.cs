namespace Infrastructure.Integrations.Samsara.Models;

public sealed class SamsaraStatsFeed
{
  public string Id { get; set; } = "";
  public List<SamsaraGps> Gps { get; set; } = [];
  public List<SamsaraEngineState> EngineStates { get; set; } = [];
  public List<SamsaraFuelPercent> FuelPercents { get; set; } = [];
}
