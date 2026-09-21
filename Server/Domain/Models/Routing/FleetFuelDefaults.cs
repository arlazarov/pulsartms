namespace Domain.Models.Routing;

public static class FleetFuelDefaults
{
  public const double TankGallons = 250;
  public const double ReserveGallons = 25;
  public const double FillPercent = 100;
  public const double DriverHourlyCostUsd = 35;
  public const double StopCostUsd = 0;

  public static PlanningPreferences Apply(PlanningPreferences source) =>
    new()
    {
      UseIfta = source.UseIfta,
      MaxDetourMinutes = source.MaxDetourMinutes,
      StopCostUsd = StopCostUsd,
      CadToUsd = source.CadToUsd,
      ReserveGallons = ReserveGallons,
      FillPercent = FillPercent,
      DriverHourlyCostUsd = DriverHourlyCostUsd,
    };
}
