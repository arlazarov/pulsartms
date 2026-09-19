namespace Client.Models.DTO.Planning;

public sealed class FuelPlan
{
  public Guid TruckId { get; set; }
  public Guid? ExecutionLegId { get; set; }
  public long AssignmentRevision { get; set; }
  public DateOnly PricingDate { get; set; }
  public bool ReusedCheckedRoute { get; set; }
  public bool EstimatedStationAccess { get; set; }
  public double StartAccessMiles { get; set; }
  public bool RemainingCostEstimate { get; set; }
  public FuelScheduleImpact? ScheduleImpact { get; set; }
  public List<Guid> DispatchIds { get; set; } = [];
  public List<string> RefreshReasons { get; set; } = [];
  public int SelectionVersion { get; set; }
  public FuelArrivalPolicy? ArrivalPolicy { get; set; }
  public double ExpectedFutureFuelCostUsd { get; set; }
  public DateTime CalculatedAt { get; set; }
  public int RouteVersion { get; set; }
  public double StartProgressMiles { get; set; }
  public bool NeedsRefresh { get; set; }

  // The plan still holds; only its prices belong to an earlier pricing day.
  public bool PricesOutOfDate { get; set; }
  public bool ManuallyEdited { get; set; }
  public string ProfileSignature { get; set; } = "";
  public double StartingGallons { get; set; }
  public double RemainingMiles { get; set; }
  public double ArrivalGallons { get; set; }
  public double PurchaseGallons { get; set; }
  public double PurchaseCostUsd { get; set; }
  public double EconomicCostUsd { get; set; }
  public double ExtraMinutes { get; set; }
  public double? SavingsUsd { get; set; }
  public bool UsesIfta { get; set; }
  public List<FuelPlanStop> Stops { get; set; } = [];
  public List<FuelStopArrival> StopArrivals { get; set; } = [];
  public List<string> Notes { get; set; } = [];
}
