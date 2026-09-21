namespace Domain.Entities.Fuel;

public class FuelDiscount : BaseEntity, ICompanyOwned
{
  public Guid CompanyId { get; set; }

  public Guid FuelStationId { get; set; }
  public FuelStation FuelStation { get; set; } = default!;
  public string Currency { get; set; } = string.Empty;
  public string Product { get; set; } = string.Empty;
  public decimal RetailPrice { get; set; }
  public decimal DiscountPrice { get; set; }
  public decimal Savings { get; set; }
  public DateOnly EffectiveFrom { get; set; }
  public DateOnly EffectiveTo { get; set; }
}
