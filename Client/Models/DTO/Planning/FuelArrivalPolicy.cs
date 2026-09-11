namespace Client.Models.DTO.Planning;

public sealed class FuelArrivalPolicy
{
  public double MinimumGallons { get; set; }
  public double TargetGallons { get; set; }
  public double ReplacementPriceUsd { get; set; }
  public double? TopUpPriceCeilingUsd { get; set; }
  public bool PoorArea { get; set; }
  public Guid EscapeStationId { get; set; }
  public string EscapeStationName { get; set; } = "";
  public double EscapeMiles { get; set; }
  public Guid? NextDispatchId { get; set; }
  public string PolicySignature { get; set; } = "";
  public string Reason { get; set; } = "";
  public List<FuelRegionCell> Regions { get; set; } = [];
}
