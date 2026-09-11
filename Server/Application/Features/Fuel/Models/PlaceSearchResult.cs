namespace Application.Features.Fuel.Models;

public class PlaceSearchResult
{
  public string PlaceId { get; set; } = string.Empty;
  public string Name { get; set; } = string.Empty;
  public string Address { get; set; } = string.Empty;
  public decimal Latitude { get; set; }
  public decimal Longitude { get; set; }
}
