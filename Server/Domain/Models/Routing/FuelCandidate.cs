namespace Domain.Models.Routing;

public sealed record FuelCandidate(
  FuelPlanStop Station,
  double AlongMiles,
  double ExtraInMiles,
  double ExtraOutMiles,
  double PriceUsd,
  double EconomicPriceUsd
)
{
  public int LegIndex { get; init; } = -1;
  public string VisitKey => $"{Station.StationId:N}:{LegIndex}";
  public double? EntryMiles { get; init; }
  public double? ExitMiles { get; init; }
}
