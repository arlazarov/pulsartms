namespace Client.Models.DTO.Planning;

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
  public double ReserveGallons { get; set; } = 25;
  public double FillPercent { get; set; } = 100;
  public double StopCostUsd { get; set; }
  public double DriverHourlyCostUsd { get; set; } = 35;
  public double MaxDetourMinutes { get; set; } = 15;
  public double? CadToUsd { get; set; }
  public bool UseIfta { get; set; } = true;
}
