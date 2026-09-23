using System.ComponentModel.DataAnnotations;

namespace Domain.Policies;

public sealed class FuelIssueOptions
{
  // How far past the end of the driver's current work period a station may
  // still be counted as this shift's. It only widens which stations are
  // selected for handing over: it is not personal conveyance, not an
  // extension of hours and not a promise of a lawful arrival. Two hours
  // covers a station just past the end of the window that a driver would
  // otherwise have to be told about separately the next morning.
  [Range(0d, 6d)]
  public double ShiftBufferHours { get; set; } = 2;

  // Hours older than this are not a reading of the driver's current state.
  [Range(1, 60)]
  public int HosFreshMinutes { get; set; } = 10;
}
