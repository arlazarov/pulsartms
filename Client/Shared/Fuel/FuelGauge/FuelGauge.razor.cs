using System.Globalization;
using Microsoft.AspNetCore.Components;

namespace Client.Shared.Fuel.FuelGauge;

public partial class FuelGauge
{
  [Parameter]
  public string Label { get; set; } = "";

  [Parameter]
  public double? Gallons { get; set; }

  [Parameter]
  public double? TankGallons { get; set; }

  [Parameter]
  public double? PercentValue { get; set; }
  private int? Percent =>
    PercentValue is { } supplied
      ? double.IsFinite(supplied) && supplied is >= 0 and <= 100
        ? (int)Math.Round(supplied)
        : null
      : Gallons is { } gallons
      && double.IsFinite(gallons)
      && gallons >= 0
      && TankGallons is { } tank
      && double.IsFinite(tank)
      && tank > 0
      && gallons <= tank
        ? (int)Math.Round(gallons / tank * 100)
        : null;
  private string PercentLabel => Percent is { } value ? $"{value}%" : "—";
  private string QuantityLabel =>
    Gallons is { } value && double.IsFinite(value)
      ? value.ToString("N0", CultureInfo.InvariantCulture)
      : "—";
}
