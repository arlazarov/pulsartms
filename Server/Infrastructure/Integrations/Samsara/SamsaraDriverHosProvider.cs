using Application.Features.Fleet.Interfaces;
using Domain.Models.Fleet;

namespace Infrastructure.Integrations.Samsara;

// The serving carrier's clocks, read through its own credentials. Nothing
// is kept here: DriverHosSnapshot keeps each carrier's clocks and runs one
// refresh per carrier at a time. The read path this had before (audit D3)
// cached every carrier's clocks under one key, so any carrier could be
// served another's, and a static gate made carriers wait for each other.
public class SamsaraDriverHosProvider(SamsaraApiService api)
  : IDriverHosRefreshProvider
{
  public async Task<
    IReadOnlyDictionary<string, DriverHosClocks>
  > RefreshClocksAsync(CancellationToken ct)
  {
    var rows = await api.GetHosClocksAsync(ct);
    var now = DateTime.UtcNow;
    return rows.Where(x =>
        x.Clocks is not null && !string.IsNullOrWhiteSpace(x.Driver.Id)
      )
      .DistinctBy(x => x.Driver.Id)
      .ToDictionary(
        x => x.Driver.Id,
        x => new DriverHosClocks
        {
          BreakMs = x.Clocks!.Break?.TimeUntilBreakDurationMs,
          DriveMs = x.Clocks.Drive?.DriveRemainingDurationMs,
          ShiftMs = x.Clocks.Shift?.ShiftRemainingDurationMs,
          CycleMs = x.Clocks.Cycle?.CycleRemainingDurationMs,
          UpdatedAt = now,
          CurrentDutyStatus = x.CurrentDutyStatus is null
            ? null
            : SamsaraHosHistoryProvider.NormalizeStatus(
              x.CurrentDutyStatus.HosStatusType
            ),
        }
      );
  }
}
