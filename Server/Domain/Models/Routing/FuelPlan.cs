namespace Domain.Models.Routing;

public sealed class FuelPlan
{
  public Guid TruckId { get; set; }
  public Guid? ExecutionLegId { get; set; }
  public long AssignmentRevision { get; set; }
  public DateOnly PricingDate { get; set; }
  public string PriceSignature { get; set; } = "";
  public string UsDiscountSignature { get; set; } = "";
  public List<DateOnly> PriceDates { get; set; } = [];
  public Dictionary<Guid, string> DispatchSignatures { get; set; } = [];
  public DateTime? FuelObservedAt { get; set; }
  public bool ManualStartingFuel { get; set; }
  public bool ManuallyEdited { get; set; }
  public bool ReusedCheckedRoute { get; set; }
  public bool EstimatedStationAccess { get; set; }
  public double StartAccessMiles { get; set; }
  public FuelScheduleImpact? ScheduleImpact { get; set; }
  public bool RemainingCostEstimate { get; set; }
  public List<Guid> DispatchIds { get; set; } = [];
  public string AssignmentSignature { get; set; } = "";
  public List<string> RefreshReasons { get; set; } = [];
  public List<FuelRouteCheck> RouteChecks { get; set; } = [];
  public int SelectionVersion { get; set; }
  public FuelArrivalPolicy? ArrivalPolicy { get; set; }
  public double ExpectedFutureFuelCostUsd { get; set; }
  public DateTime CalculatedAt { get; set; }
  public int RouteVersion { get; set; }
  public double StartProgressMiles { get; set; }
  public bool NeedsRefresh { get; set; }

  // A plan whose route, assignments and truck position still hold but whose
  // prices belong to an earlier pricing day remains the plan to drive: the
  // station and the volume do not change, only what the fuel costs. Hiding it
  // leaves a dispatcher unable to tell "recalculating" from "nowhere to go".
  public bool PricesOutOfDate { get; set; }

  // The same idea for the truck's position. A plan whose stops and prices
  // hold does not stop being the plan to drive because the last GPS fix is
  // old or missing: only the "how far along is he" figures are unknown, and
  // the driver still has to fuel somewhere. Hiding it leaves him with
  // nothing, which is the worse answer.
  public bool PositionUnverified { get; set; }
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
