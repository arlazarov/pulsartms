namespace Client.Models.DTO.Planning;

public sealed record FuelPlanEditStop(Guid StationId, Guid? BeforeStopId, double BuyGallons, bool FillToTarget)
{
    public double? PurchaseLimitGallons { get; init; }
}
public sealed record FuelPlanEditRequest(DateTime? ExpectedCalculatedAt, List<FuelPlanEditStop>? Stops);
public sealed record FuelPlanEditSegment(Guid BeforeStopId, PlanStop? AfterStop, PlanStop BeforeStop, Guid DispatchId);
public sealed record FuelPlanEditPreview(FuelPlan Plan, List<FuelPlanEditStop> Stops,
    DateTime? ExpectedCalculatedAt, double TankGallons, double FillLimitGallons, List<string> Errors,
    bool ValuesAvailable = true, List<FuelPlanEditSegment>? Segments = null);
public sealed record FuelPlanResetRequest(DateTime? ExpectedCalculatedAt);
