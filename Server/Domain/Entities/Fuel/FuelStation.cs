namespace Domain.Entities.Fuel;

public class FuelStation : BaseEntity
{
  public string ExternalId { get; set; } = string.Empty;
  public string Name { get; set; } = string.Empty;
  public string Address { get; set; } = string.Empty;
  public string City { get; set; } = string.Empty;
  public string Region { get; set; } = string.Empty;
  public string PostalCode { get; set; } = string.Empty;
  public string Country { get; set; } = string.Empty;
  public decimal? Latitude { get; set; }
  public decimal? Longitude { get; set; }

  // What the place provider last said about the business still existing:
  // OPERATIONAL, CLOSED_TEMPORARILY or CLOSED_PERMANENTLY. Empty means
  // nobody has asked yet, which is not the same as open - an unasked station
  // has not been cleared, only unasked, and planning must tell the two apart.
  public string BusinessStatus { get; set; } = string.Empty;
  public DateTime? StatusCheckedAt { get; set; }

  // The provider's own identifier for this place, kept so a re-check reads
  // the same place rather than searching for it again. A repeated text
  // search can land on a different business next door and replace one
  // station's status with another's.
  public string PlaceId { get; set; } = string.Empty;

  // The provider's weekly opening periods and the station's offset from UTC,
  // kept together because the periods are local time and mean nothing alone.
  // Null is "not stated", which FuelStationHours answers Unknown for rather
  // than refusing the station.
  public string? OpeningHoursJson { get; set; }
  public int? UtcOffsetMinutes { get; set; }

  public ICollection<FuelDiscount> FuelDiscounts { get; set; } = [];
  public ICollection<FuelTransaction> FuelTransactions { get; set; } = [];
}
