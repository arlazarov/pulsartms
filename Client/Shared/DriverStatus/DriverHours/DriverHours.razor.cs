using System.Globalization;
using Client.Models.DTO.Planning;
using Microsoft.AspNetCore.Components;

namespace Client.Shared.DriverStatus.DriverHours;

public partial class DriverHours
{
  [Parameter]
  public DriverHosClocks? Clocks { get; set; }

  [Parameter]
  public bool ShowDutyStatus { get; set; } = true;

  // Dials carry a clock's shape at a glance and cost the height of one.
  // Where four of them would take a row the card cannot spare, the same
  // four clocks read as text on one line.
  [Parameter]
  public bool Dials { get; set; } = true;

  // Text clocks with a thin bar of what is left, as the workspace's clock
  // cells draw them.
  [Parameter]
  public bool Bars { get; set; }

  [Parameter]
  public DriverDutyStatus? Status { get; set; }

  // The ruleset the server read the hours under, given directly or by the
  // duty status the server sent with them. The eight-hour break is a US
  // rule: under Canadian rules its clock says nothing and is left out.
  // Unknown keeps every clock the provider reports.
  [Parameter]
  public string? Jurisdiction { get; set; }
  private string? Rules => Jurisdiction ?? Status?.Jurisdiction;

  private IEnumerable<(string, long?, int)> Readings =>
    new[]
    {
      ("Break", Clocks?.BreakMs, 8),
      ("Drive", Clocks?.DriveMs, 11),
      ("Shift", Clocks?.ShiftMs, 14),
      ("Cycle", Clocks?.CycleMs, 70),
    }.Where(clock => clock.Item1 != "Break" || Rules != "CA");

  private static string Fill(long? milliseconds, int hours) =>
    Math.Clamp((milliseconds ?? 0) / (hours * 3600000d) * 100, 0, 100)
      .ToString("0.###", CultureInfo.InvariantCulture);

  private static string Format(long? milliseconds)
  {
    if (milliseconds is null)
      return "—";
    var minutes = Math.Max(0, milliseconds.Value) / 60000;
    return $"{minutes / 60}:{minutes % 60:00}";
  }
}
