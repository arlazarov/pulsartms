using System.ComponentModel.DataAnnotations;

namespace Application.Features.Fuel.Options;

// Asking the place provider whether a station still exists costs money per
// request, and there are hundreds of stations, so the rate is a setting
// rather than a constant. The defaults sweep every station within about two
// days and then re-ask each one about twice a month.
public sealed class FuelStationStatusOptions
{
  public bool Enabled { get; set; } = true;

  [Range(1, 1440)]
  public int IntervalMinutes { get; set; } = 30;

  // How many stations one pass may ask about. The pass stops at this many
  // whether or not more are due, so a backlog drains at a known rate instead
  // of arriving as one bill.
  [Range(1, 200)]
  public int BatchSize { get; set; } = 15;

  // How stale an answer may be before it is asked again. A station that shut
  // is found within this long at worst.
  [Range(1, 365)]
  public int RecheckDays { get; set; } = 14;
}
