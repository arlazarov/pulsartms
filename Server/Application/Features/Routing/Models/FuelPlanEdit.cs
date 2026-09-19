namespace Application.Features.Routing.Models;

public sealed record FuelPlanEditStop(
  Guid StationId,
  Guid? BeforeStopId,
  double BuyGallons,
  bool FillToTarget
)
{
  public double? PurchaseLimitGallons { get; init; }
}

public sealed record FuelPlanEditRequest(
  DateTime? ExpectedCalculatedAt,
  List<FuelPlanEditStop>? Stops,
  int? QuantityStopIndex = null
)
{
  public Guid? ExecutionLegId { get; init; }
  public long? AssignmentRevision { get; init; }
}

public sealed record FuelPlanEditPreview(
  FuelPlan Plan,
  List<FuelPlanEditStop> Stops,
  DateTime? ExpectedCalculatedAt,
  double TankGallons,
  double FillLimitGallons,
  List<string> Errors,
  bool ValuesAvailable = true,
  List<FuelPlanEditSegment>? Segments = null,
  FuelQuantityChoices? QuantityChoices = null
);

public sealed record FuelQuantityChoices(
  int StopIndex,
  List<FuelQuantityOption> Options
);

public sealed record FuelQuantityOption(
  double SliderGallons,
  bool FillToTarget,
  List<FuelQuantityVisit> Visits,
  double ArrivalGallons,
  double PurchaseGallons,
  double PurchaseCostUsd,
  double EconomicCostUsd,
  double ExpectedFutureFuelCostUsd,
  List<string> Errors
);

public sealed record FuelQuantityVisit(
  double ArrivalGallons,
  double BuyGallons,
  double DepartureGallons,
  bool FillToTarget,
  double? PurchaseLimitGallons,
  double? PurchaseCostUsd
);

public sealed record FuelPlanEditSegment(
  Guid BeforeStopId,
  PlanStop? AfterStop,
  PlanStop BeforeStop,
  Guid DispatchId
);

public sealed record FuelManualReplayResult(
  FuelPlan Plan,
  List<string> Errors,
  IReadOnlyList<double?> PurchaseLimitsGallons
);
