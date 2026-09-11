namespace Application.Features.Routing.Models;

public sealed class FuelPlanStop
{
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
  public double? PurchaseCostUsd => double.IsFinite(BuyGallons) && BuyGallons >= 0
    && double.IsFinite(CashUsdPerGallon) && CashUsdPerGallon > 0
    && double.IsFinite(BuyGallons * CashUsdPerGallon) ? BuyGallons * CashUsdPerGallon : null;
  public double DepartureGallons { get; set; }
  public bool FillToTarget { get; set; }
  public double YourPrice { get; set; }
  public double EconomicPrice { get; set; }
  public string Currency { get; set; } = "";
  public string Unit { get; set; } = "";
  public double DetourMiles { get; set; }
  public double DetourMinutes { get; set; }
  public DateOnly PriceDate { get; set; }
}
