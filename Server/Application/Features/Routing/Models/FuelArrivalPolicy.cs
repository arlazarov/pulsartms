namespace Application.Features.Routing.Models;

public sealed class FuelArrivalPolicy
{
  public double MinimumGallons { get; set; }
  public double TargetGallons { get; set; }
  public double ReplacementPriceUsd { get; set; }
  public double? TopUpPriceCeilingUsd { get; set; }
  public bool PoorArea { get; set; }
  public bool EconomicPurchasesOnly { get; set; }
  public Guid EscapeStationId { get; set; }
  public string EscapeStationName { get; set; } = "";
  public double EscapeMiles { get; set; }
  public Guid? NextDispatchId { get; set; }
  public string PolicySignature { get; set; } = "";
  public string Reason { get; set; } = "";
  public List<FuelRegionCell> Regions { get; set; } = [];
}
