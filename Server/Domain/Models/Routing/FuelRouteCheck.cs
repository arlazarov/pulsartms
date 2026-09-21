namespace Domain.Models.Routing;

public sealed class FuelRouteCheck
{
  public List<string> Stations { get; set; } = [];
  public double? ExtraMiles { get; set; }
  public double? ExtraMinutes { get; set; }
  public double? CostUsd { get; set; }
  public string Result { get; set; } = "";
}
