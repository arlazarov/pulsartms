using Domain.Models.Routing;

namespace Domain.Rules.Routing;

public static class FuelAccessEstimate
{
  public const double NearbyMiles = 40;
  public const double CurrentPositionToleranceMiles = NearbyMiles;

  // A planning allowance, not a verified road connection or a distance upper
  // bound.
  public static double DistanceMiles(double direct) =>
    direct <= .05 ? 0 : Math.Max(.5, direct * 1.5);

  public static double DrivingMinutes(double miles) =>
    miles <= 0 ? 0 : miles * 2 + 2;

  public static List<FuelCandidate> Nearby(
    IEnumerable<FuelCandidate> candidates
  ) =>
    candidates
      .Where(x =>
        double.IsFinite(x.ExtraInMiles)
        && x.ExtraInMiles >= 0
        && x.ExtraInMiles <= NearbyMiles
      )
      .Select(x =>
      {
        var access = DistanceMiles(x.ExtraInMiles);
        var source = x.Station;
        return x with
        {
          ExtraInMiles = access,
          ExtraOutMiles = access,
          Station = new FuelPlanStop
          {
            StationId = source.StationId,
            Name = source.Name,
            Address = source.Address,
            Country = source.Country,
            Point = source.Point,
            YourPrice = source.YourPrice,
            EconomicPrice = source.EconomicPrice,
            Currency = source.Currency,
            Unit = source.Unit,
            PriceDate = source.PriceDate,
            DetourMinutes = DrivingMinutes(access * 2),
          },
        };
      })
      .ToList();

  public static TruckRoute TimingRoute(
    TruckRoute baseline,
    IReadOnlyList<FuelCandidate> purchases,
    double initialAccessMiles = 0
  )
  {
    var byLeg = purchases
      .GroupBy(x => x.LegIndex)
      .ToDictionary(x => x.Key, x => x.ToArray());
    var legs = baseline
      .Legs.Select(
        (leg, index) =>
        {
          var visits = byLeg.GetValueOrDefault(index) ?? [];
          var initial = index == 0 ? initialAccessMiles : 0;
          if (visits.Length == 0 && initial == 0)
            return leg;
          return new RouteLeg(
            leg.Miles
              + initial
              + visits.Sum(x => x.ExtraInMiles + x.ExtraOutMiles),
            leg.Seconds
              + (
                DrivingMinutes(initial)
                + visits.Sum(x => x.Station.DetourMinutes)
              ) * 60,
            leg.Points
          );
        }
      )
      .ToList();
    return new()
    {
      CalculatedAt = baseline.CalculatedAt,
      Miles = legs.Sum(x => x.Miles),
      Seconds = legs.Sum(x => x.Seconds),
      Legs = legs,
      Warnings = baseline.Warnings,
    };
  }
}
