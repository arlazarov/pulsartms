namespace Client.Models.DTO.Planning;

public sealed class FuelPlanStop
{
  public string Warning { get; set; } = "";
  public int Number { get; set; }
  public string VisitKey { get; set; } = "";
  public Guid DispatchId { get; set; }
  public Guid BeforeStopId { get; set; }
  public double CashUsdPerGallon { get; set; }
  public double EconomicUsdPerGallon { get; set; }
  public double? CurrentRouteMile { get; set; }
  public Guid StationId { get; set; }
  public string Name { get; set; } = "";
  public string Address { get; set; } = "";
  public RoutePoint Point { get; set; } = new(0, 0);
  public double MilesAhead { get; set; }
  public double? RouteMilesAhead { get; set; }
  public double ArrivalGallons { get; set; }
  public double BuyGallons { get; set; }
  public double? PurchaseCostUsd { get; set; }
  public double DepartureGallons { get; set; }
  public bool FillToTarget { get; set; }
  public double YourPrice { get; set; }
  public double EconomicPrice { get; set; }
  public string Currency { get; set; } = "";
  public string Unit { get; set; } = "";
  public double DetourMiles { get; set; }
  public double DetourMinutes { get; set; }
  public DateOnly PriceDate { get; set; }
  public DateTimeOffset? EstimatedArrival { get; set; }
  public bool PriceEstimated { get; set; }
  public string? IssueHorizon { get; set; }
  public FuelSendStatus? Sent { get; set; }
}
