using System.ComponentModel.DataAnnotations;

namespace Application.Features.Eta.Options;

public sealed class EtaPlanningOptions
{
  [Range(1d, 11d)] public double DrivingHoursPerShift { get; set; } = 11;
  [Range(15, 60)] public int PreTripMinutes { get; set; } = 15;
  [Range(5, 120)] public int FuelStopMinutes { get; set; } = 5;
  [Range(30, 120)] public int DailyBreakMinutes { get; set; } = 30;
  [Range(0, 720)] public int PickupMinutes { get; set; } = 120;
  [Range(0, 720)] public int DeliveryMinutes { get; set; } = 120;
  [Range(0d, 100d)] public double TravelTimeBufferPercent { get; set; } = 0;
  [Range(20d, 75d)] public double PlanningSpeedCapMph { get; set; } = 60;
  [Range(1d, 100d)] public double OffRouteEstimateMaxMiles { get; set; } = 25;
  [Range(1d, 3d)] public double OffRouteDistanceFactor { get; set; } = 1.25;

  public double TravelHours(double miles, double roadSeconds) =>
    Math.Max(roadSeconds / 3600, miles / PlanningSpeedCapMph) * (1 + TravelTimeBufferPercent / 100);
}
