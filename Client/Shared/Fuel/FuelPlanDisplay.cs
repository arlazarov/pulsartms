using System.Globalization;
using Client.Models.DTO.Planning;

namespace Client.Shared.Fuel;

// How a fuel plan's server figures read on a card: gallons as whole
// numbers, money in dollars, a station's price in its own currency and
// unit. Formatting only; every figure comes from the plan.
public static class FuelPlanDisplay
{
  public static string Quantity(double? value) =>
    value is { } number && double.IsFinite(number)
      ? number.ToString("N0", CultureInfo.InvariantCulture)
      : "—";

  public static string Money(double? value) =>
    value is { } number && double.IsFinite(number)
      ? $"${number.ToString("N2", CultureInfo.InvariantCulture)}"
      : "—";

  public static string Price(FuelPlanStop stop) =>
    stop.YourPrice > 0 && double.IsFinite(stop.YourPrice)
      ? $"{stop.YourPrice.ToString("N3", CultureInfo.InvariantCulture)} {stop.Currency} / {stop.Unit}"
      : "—";
}
