using Application.Features.Eta.Interfaces;
using Application.Features.Fleet.Services;
using Application.Features.Routing.Services.Routes;
using Domain.Models.Eta;
using Domain.Rules.Eta;

namespace Application.Features.Eta.Services;

// When a driver's current duty status began, and the ongoing rest, under
// the rule the forecasts use. The history is the forecasts' own: read
// through the provider's gate and one-minute cache, so a reader costs at
// most one history read per driver a minute, and none when a forecast read
// it lately. Needs no route: the status, its start and the daily rest come
// from the driver's history and clocks alone. Null when that history is
// unavailable, stale or disagrees with the clocks - never the time the
// clocks were fetched.
//
// The ruleset (and with it the cycle reset) is the one the truck's
// planning summary carries: the region its forecast found the truck in,
// read through the same guarded summary the Fleet Map shows (without its
// geometry), with its assignment and input checks. It counts only while
// that forecast is still valid and only for the driver the truck is
// assigned to; it is unknown for a driver on no truck or on several, or
// while the summary has no current forecast - never guessed from a
// carrier's country or a position read here. The summary's read copy of
// the truck's inputs can lag a reassignment by up to ReadCacheSeconds.
public sealed class DriverDutyReader(
  DriverClockReader reader,
  IHosHistoryProvider history,
  PlanningSummaryReader summaries,
  TimeProvider clock
)
{
  public async Task<DriverDutyStatus?> ReadAsync(
    Guid driverId,
    Guid? truckId,
    CancellationToken ct
  )
  {
    if (await reader.DriverAsync(driverId, ct) is not { } driver)
      return null;
    var external = driver.ExternalId;
    var clocks = await reader.ClocksAsync(ct);
    var now = clock.GetUtcNow();
    return HosDutyStatus.Read(
      await history.GetAsync(external, ct),
      clocks.GetValueOrDefault(external),
      now,
      truckId is { } truck && driver.Trucks.Contains(truck)
        ? Current(
          (await summaries.ForTruckAsync(truck, ct, summaryOnly: true))
            .State
            ?.Eta,
          now
        )
        : null
    );
  }

  // A retained forecast past its own validity says where the truck was,
  // not which rules apply now.
  private static string? Current(DispatchEta? eta, DateTimeOffset now) =>
    eta is { ValidUntil: var until } && until >= now.UtcDateTime
      ? eta.DutyStatus?.Jurisdiction
      : null;
}
