namespace Client.Models.DTO.Planning;

public sealed class FuelRecommendations
{
  public bool AccessProblem { get; set; }
  public string SettingsSignature { get; set; } = "";
  public int Version { get; set; }
  public DateTime CalculatedAt { get; set; }
  public string Message { get; set; } = "";
  public List<RecommendedFuelStation> Stations { get; set; } = [];
}
