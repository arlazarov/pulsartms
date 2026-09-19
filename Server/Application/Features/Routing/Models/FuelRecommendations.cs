namespace Application.Features.Routing.Models;

public sealed class FuelRecommendations
{
  public FuelObservationStamp? Observation { get; set; }
  public string WorkSignature { get; set; } = "";
  public FuelHistoryDependencies? HistoryDependencies { get; set; }
  public List<SavedRoadVersion> RoadDependencies { get; set; } = [];

  public bool MatchesTelemetry(RoutePlanningState state) =>
    Observation?.Matches(state) == true;

  public bool AccessProblem { get; set; }
  public string SettingsSignature { get; set; } = "";
  public int Version { get; set; }
  public DateTime CalculatedAt { get; set; }
  public string Message { get; set; } = "";
  public List<RecommendedFuelStation> Stations { get; set; } = [];
}
