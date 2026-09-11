namespace Client.Models.DTO.Planning;

public sealed record NextLoadStop(double Latitude, double Longitude, string Job, string Name = "")
{
  public Guid Id { get; init; }
}
public sealed record NextLoadRoute(Guid Id, int LoadNumber, string Status,
  IReadOnlyList<RouteLeg> Legs, IReadOnlyList<NextLoadStop> Stops, NextLoadConnection? Deadhead = null, int StopCount = 0);
