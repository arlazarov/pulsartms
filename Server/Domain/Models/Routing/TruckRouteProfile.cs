namespace Domain.Models.Routing;

public sealed class TruckRouteProfile
{
  public const double StandardTrailerFeet = 53;
  public double TrailerLengthFeet { get; set; } = StandardTrailerFeet;
  public double HeightFeet { get; set; } = 13.5;
  public double WidthFeet { get; set; } = 8.5;
  public double LengthFeet { get; set; } = 72;
  public double WeightPounds { get; set; } = 80000;
  public int Axles { get; set; } = 5;
  public double AxleWeightPounds { get; set; } = 20000;
  public string Hazmat { get; set; } = "";
  public bool Confirmed { get; set; }
  public bool UsesFleetDefaults { get; set; }
  public double? TankGallons { get; set; }
  public double? Mpg { get; set; }
  public double ReserveGallons { get; set; } = FleetFuelDefaults.ReserveGallons;
  public double FillPercent { get; set; } = FleetFuelDefaults.FillPercent;
  public double StopCostUsd { get; set; } = FleetFuelDefaults.StopCostUsd;
  public double DriverHourlyCostUsd { get; set; } =
    FleetFuelDefaults.DriverHourlyCostUsd;
  public double MaxDetourMinutes { get; set; } = 15;
  public double? CadToUsd { get; set; }
  public bool UseIfta { get; set; } = true;

  public TruckRouteProfile Copy() => (TruckRouteProfile)MemberwiseClone();

  public string? Validate(bool fuel = false)
  {
    if (!Confirmed && !UsesFleetDefaults)
      return "Truck routing dimensions are unavailable.";
    if (
      !In(HeightFeet, 5, 20)
      || !In(WidthFeet, 4, 12)
      || !In(LengthFeet, 10, 120)
      || !In(WeightPounds, 5000, 150000)
      || Axles is < 2 or > 12
      || !In(AxleWeightPounds, 1000, 30000)
    )
      return "Enter valid truck dimensions, loaded weight and axle limits.";
    if (
      Hazmat != ""
      && !Enumerable
        .Range(1, 9)
        .Select(x => $"USHazmatClass{x}")
        .Contains(Hazmat)
    )
      return "Select a supported hazardous cargo class.";
    if (!fuel)
      return null;
    if (
      TankGallons is not { } tank
      || !In(tank, 20, 500)
      || Mpg is not { } mpg
      || !In(mpg, 2, 15)
    )
      return "Set the total tank capacity and expected MPG for this truck.";
    if (
      !In(FillPercent, 50, 100)
      || !In(ReserveGallons, 5, 100)
      || ReserveGallons >= tank * FillPercent / 100
    )
      return "Fuel reserve must be below the target fill level.";
    if (
      !In(StopCostUsd, 0, 200)
      || !In(DriverHourlyCostUsd, 0, 300)
      || !In(MaxDetourMinutes, 1, 60)
    )
      return "Check the stop cost, hourly cost and maximum detour.";
    if (CadToUsd.HasValue && !In(CadToUsd.Value, .1, 2))
      return "Enter USD per 1 CAD, or leave it blank to use the official published rate.";
    return null;
  }

  private static bool In(double value, double min, double max) =>
    double.IsFinite(value) && value >= min && value <= max;
}
