namespace Application.Features.Routing.Models;

public sealed class FuelRecommendations
{
  public string SettingsSignature { get; set; } = "";
  public int Version { get; set; }
  public DateTime CalculatedAt { get; set; }
  public string Message { get; set; } = "";
  public List<RecommendedFuelStation> Stations { get; set; } = [];
}
