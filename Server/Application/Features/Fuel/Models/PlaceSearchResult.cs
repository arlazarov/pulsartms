namespace Application.Features.Fuel.Models;

public class PlaceSearchResult
{
  public string PlaceId { get; set; } = string.Empty;
  public string Name { get; set; } = string.Empty;
  public string Address { get; set; } = string.Empty;
  public decimal Latitude { get; set; }
  public decimal Longitude { get; set; }

  // What the place provider says about the business still existing:
  // OPERATIONAL, CLOSED_TEMPORARILY or CLOSED_PERMANENTLY. Empty when the
  // lookup predates this field, which is not the same as operational - a
  // station nobody has asked about yet has not been cleared, only unasked.
  public string BusinessStatus { get; set; } = string.Empty;

  // The provider's weekly opening periods, kept as it gave them. Null means
  // it said nothing, which is not the same as "always open": a station whose
  // hours are unknown must not be refused, only not vouched for.
  public string? OpeningHoursJson { get; set; }

  // Minutes the station's local time is ahead of UTC. The periods are stated
  // in local time and mean nothing without it.
  public int? UtcOffsetMinutes { get; set; }
}
