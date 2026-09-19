namespace Application.Features.Fuel.Models;

// Configured under FuelDiscounts:Source. Customers without a fuel card run "none";
// other card programs add a source here and an Infrastructure provider behind IFuelDiscountProvider.
public static class FuelDiscountSources
{
  public const string BvdGmail = "bvd-gmail";
  public const string None = "none";
}
