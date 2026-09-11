namespace Client.Models.DTO.Planning;

public sealed class FuelRegionCell
{
  public string Id { get; set; } = "";
  public double South { get; set; }
  public double North { get; set; }
  public double West { get; set; }
  public double East { get; set; }
  public int StationCount { get; set; }
  public double? MedianPriceUsd { get; set; }
  public string Kind { get; set; } = "unknown";
}
