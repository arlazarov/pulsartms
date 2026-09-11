namespace Domain.Entities.Fuel;

public class IftaTaxRate : BaseEntity
{
  public string Jurisdiction { get; set; } = string.Empty;
  public string FuelType { get; set; } = string.Empty;
  public decimal Rate { get; set; }
  public string Currency { get; set; } = string.Empty;
  public string Unit { get; set; } = string.Empty;
  public DateOnly EffectiveFrom { get; set; }
  public DateOnly EffectiveTo { get; set; }
}
