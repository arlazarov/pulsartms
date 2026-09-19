namespace Application.Features.Routing.Models;

public sealed record NextLoadStop(
  double Latitude,
  double Longitude,
  string Job,
  string Name = ""
)
{
  public Guid Id { get; init; }
  public string StateAfter { get; init; } = "Unknown";
  public long OperationRevision { get; init; }
}

public sealed record NextLoadRoute(
  Guid Id,
  int LoadNumber,
  string Status,
  IReadOnlyList<RouteLeg> Legs,
  IReadOnlyList<NextLoadStop> Stops,
  NextLoadConnection? Deadhead = null,
  int StopCount = 0
)
{
  public Guid? ExecutionLegId { get; init; }
}
