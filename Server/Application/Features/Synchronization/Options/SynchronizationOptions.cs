using System.ComponentModel.DataAnnotations;

namespace Application.Features.Synchronization.Options;

public sealed class SynchronizationOptions
{
  public bool Enabled { get; set; }
  public bool HighFrequencyLocations { get; set; } = true;
  [Range(30, 86400)] public int AssignmentsSeconds { get; set; } = 60;
  [Range(30, 86400)] public int DispatchSeconds { get; set; } = 60;
  [Range(300, 86400)] public int CatalogSeconds { get; set; } = 3600;
  [Range(5, 300)] public int TelemetrySeconds { get; set; } = 60;
  // Longest a browser telemetry poll is held waiting for a new snapshot; must stay under the hosting
  // rewrite timeout (Firebase Hosting to Cloud Run: 60 seconds). 0 disables holding.
  [Range(0, 55)] public int LocationWaitSecondsMax { get; set; } = 25;
  [Range(30, 3600)] public int CheckpointSeconds { get; set; } = 120;
  [Range(15, 3600)] public int PlanningSeconds { get; set; } = 30;
  [Range(30, 3600)] public int OnDemandPlanningSeconds { get; set; } = 120;
  [Range(1, 50)] public double RouteDeviationMiles { get; set; } = 2;
  [Range(30, 1800)] public int RouteDeviationSeconds { get; set; } = 180;
  [Range(30, 600)] public int ReadCacheSeconds { get; set; } = 120;
  [Range(1, 60)] public int SessionValidationSeconds { get; set; } = 30;
  [Range(30, 600)] public int RetrySeconds { get; set; } = 60;
  [Range(60, 900)] public int JobTimeoutSeconds { get; set; } = 180;
  [Range(0, 5)] public int UpcomingRoutesPerTruck { get; set; } = 1;
  [Range(1, 50)] public int MaxTrucksPerPlanningCycle { get; set; } = 10;
  [Range(1, 30)] public int DispatchLookbackDays { get; set; } = 30;
  [Range(1, 30)] public int DispatchLookaheadDays { get; set; } = 7;
}
