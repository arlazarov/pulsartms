using Client.Models;
using Client.Models.DTO.Planning;
using Client.Services;
using Client.Shared.Fuel;
using Microsoft.AspNetCore.Components;

namespace Client.Pages.FleetMap;

// The selected truck's fuel plan as a card of its own: the tank now, what
// the plan buys and what it costs, then each planned stop in order with
// the tank on arrival, the purchase and its price. Every figure is the
// server's; this card formats them and hands its actions to the page,
// which owns the editor, the send window and the station cards.
public partial class FleetFuelPlan
{
  [CascadingParameter]
  public DisplayUnits Units { get; set; } = DisplayUnits.Default;

  // Null when there is no usable plan to show.
  [Parameter]
  public FuelPlan? Plan { get; set; }

  // A plan exists but is being recalculated, rather than no plan at all.
  [Parameter]
  public bool Unusable { get; set; }

  [Parameter]
  public double? CurrentPercent { get; set; }

  [Parameter]
  public double? TankGallons { get; set; }

  [Parameter]
  public bool CanEdit { get; set; }

  [Parameter]
  public bool CanSend { get; set; }

  [Parameter]
  public EventCallback<FuelPlanStop> ViewStation { get; set; }

  [Parameter]
  public EventCallback Edit { get; set; }

  [Parameter]
  public EventCallback Send { get; set; }

  [Parameter]
  public EventCallback Stations { get; set; }

  private bool KnownTank =>
    TankGallons is > 0 and var tank && double.IsFinite(tank);

  // The tank now: the telemetry's percentage, and the gallons it stands
  // for in this truck's tank.
  private string CurrentFuel =>
    CurrentPercent is { } percent && double.IsFinite(percent)
      ? KnownTank
        ? $"{Math.Round(percent)}% · {FuelPlanDisplay.Quantity(percent / 100 * TankGallons)} gal"
        : $"{Math.Round(percent)}%"
      : "—";

  // What the plan says the tank holds on arrival, with its share of the
  // tank when the tank is known.
  private string Tank(double gallons) =>
    !double.IsFinite(gallons) ? "—"
    : KnownTank && gallons >= 0 && gallons <= TankGallons
      ? $"{Math.Round(gallons / TankGallons!.Value * 100)}% · {FuelPlanDisplay.Quantity(gallons)} gal"
    : $"{FuelPlanDisplay.Quantity(gallons)} gal";

  private static string AddressLine(string address)
  {
    var lines = StopAddressLines.Create(address);
    return lines.Locality.Length > 0
      ? $"{lines.Street}, {lines.Locality}"
      : lines.Street;
  }

  private static string StateLabel(FuelPlan plan) =>
    plan.NeedsRefresh && !plan.PricesOutOfDate ? "Updating"
    : plan.PricesOutOfDate ? "Earlier prices"
    : plan.ManuallyEdited ? "Saved plan"
    : "Automatic plan";

  private static string StateTitle(FuelPlan plan) =>
    plan.PricesOutOfDate
      ? $"The plan holds; its prices are from {plan.PricingDate:MMM d}."
      : plan.ManuallyEdited
        ? "A dispatcher saved this plan."
        : "Calculated automatically for this load.";
}
