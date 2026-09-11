using System.ComponentModel.DataAnnotations;

namespace Client.Models.DTO.Planning;

public sealed class PlanningPreferences
{
  public bool UseIfta { get; set; } = true;
  [Range(1d, 60d, ErrorMessage = "Maximum detour must be between 1 and 60 minutes.")]
  public double MaxDetourMinutes { get; set; } = 15;
  [Range(5d, 100d, ErrorMessage = "Fuel reserve must be between 5 and 100 gallons.")]
  public double ReserveGallons { get; set; } = 25;
  [Range(50d, 100d, ErrorMessage = "Fill target must be between 50% and 100%.")]
  public double FillPercent { get; set; } = 100;
  [Range(0d, 200d, ErrorMessage = "Cost per stop must be between $0 and $200.")]
  public double StopCostUsd { get; set; }
  [Range(0d, 300d, ErrorMessage = "Driver time cost must be between $0 and $300 per hour.")]
  public double DriverHourlyCostUsd { get; set; } = 35;
  [Range(.1, 2, ErrorMessage = "The CAD exchange rate must be between 0.1 and 2 USD per CAD.")]
  public double? CadToUsd { get; set; }
}
