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
  public ICollection<FuelDiscount> FuelDiscounts { get; set; } = [];
  public ICollection<FuelTransaction> FuelTransactions { get; set; } = [];
}
