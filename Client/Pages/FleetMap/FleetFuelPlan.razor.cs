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

  // A saved plan exists but has no stops to show.
  [Parameter]
  public bool Unusable { get; set; }

  // The plan shown is the last saved one, kept until its replacement is
  // published; Updating only while a recalculation actually runs here.
  [Parameter]
  public bool Stale { get; set; }

  [Parameter]
  public bool Updating { get; set; }

  [Parameter]
  public bool UpdateFailed { get; set; }

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

  // The load's own stops, so the plan reads as the drive it belongs to:
  // each fuel stop stands before the stop it is planned before.
  [Parameter]
  public IReadOnlyList<PlanStop> Stops { get; set; } = [];

  // The stop the truck is going to; the ones before it are behind it.
  [Parameter]
  public Guid? NextStopId { get; set; }

  // With the next loads not shown, only the fuel stops of this dispatch
  // are listed - and any before one of its own stops, which is on this
  // road whatever load it is booked to; the plan's totals stay the plan's.
  [Parameter]
  public Guid? OnlyDispatch { get; set; }

  private sealed record Entry(
    FuelPlanStop? Fuel,
    PlanStop? Stop,
    FuelPlanStop? Previous
  );

  private List<Entry> Timeline(FuelPlan plan)
  {
    var remaining = Stops.OrderBy(stop => stop.Sequence).ToList();
    var from = NextStopId is { } next
      ? remaining.FindIndex(stop => stop.Id == next)
      : 0;
    if (from > 0)
      remaining = remaining.Skip(from).ToList();
    var known = remaining.Select(stop => stop.Id).ToHashSet();
    var shown = plan
      .Stops.Where(f =>
        OnlyDispatch is null
        || f.DispatchId == OnlyDispatch
        || f.DispatchId == Guid.Empty
        || known.Contains(f.BeforeStopId)
      )
      .ToList();
    var entries = new List<Entry>();
    var placed = new HashSet<FuelPlanStop>();
    FuelPlanStop? previous = null;
    void Add(FuelPlanStop fuel)
    {
      entries.Add(new(fuel, null, previous));
      previous = fuel;
      placed.Add(fuel);
    }
    // A fuel stop before a stop the truck has passed, or one the plan does
    // not name, is not lost: it comes first.
    foreach (var fuel in shown.Where(f => !known.Contains(f.BeforeStopId)))
      Add(fuel);
    foreach (var stop in remaining)
    {
      foreach (
        var fuel in shown.Where(f =>
          f.BeforeStopId == stop.Id && !placed.Contains(f)
        )
      )
        Add(fuel);
      entries.Add(new(null, stop, null));
    }
    return entries;
  }

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

  private string StateLabel(FuelPlan plan) =>
    Updating ? "Updating"
    : Stale && UpdateFailed ? "Update failed"
    : Stale ? "Needs update"
    : plan.PricesOutOfDate ? "Earlier prices"
    : plan.ManuallyEdited ? "Saved plan"
    : "Automatic plan";

  private string StateTitle(FuelPlan plan) =>
    Updating ? "Recalculating fuel; the saved plan stays until it is replaced."
    : Stale && UpdateFailed
      ? "The last recalculation failed. This is the last saved plan; its "
        + "stations stay until a new plan is published."
    : Stale
      ? "The last saved plan, kept until a new one is published. "
        + string.Join(" ", plan.RefreshReasons)
    : plan.PricesOutOfDate
      ? $"The plan holds; its prices are from {plan.PricingDate:MMM d}."
    : plan.ManuallyEdited ? "A dispatcher saved this plan."
    : "Calculated automatically for this load.";
}
