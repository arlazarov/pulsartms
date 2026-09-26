using Application.Features.Eta.Interfaces;
using Application.Features.Fleet.Interfaces;
using Application.Features.Routing.Services.Routes;
using Application.Models;
using Domain.Models.Eta;
using Domain.Rules.Eta;

namespace Application.Features.Eta.Queries;

// When a driver's current duty status began, for a page that shows how
// long they have been in it, under the rule the forecasts use. The history
// is the forecasts' own: read through the provider's gate and one-minute
// cache, so opening a conversation costs at most one history read per
// driver a minute, and none when a forecast read it lately. StartedAt is
// null when that history is unavailable, stale or disagrees with the
// clocks - never the time the clocks were fetched.
//
// The ruleset the hours are read under is the one the truck's planning
// summary carries: the region its forecast found the truck in, read
// through the same guarded summary the Fleet Map shows (without its
// geometry), with its assignment and input checks. It counts only while
// that forecast is still valid and only for the driver the truck is
// assigned to; it is unknown for a driver on no truck or on several, or
// while the summary has no current forecast - never guessed from a
// carrier's country or a position read here. The summary's read copy of
// the truck's inputs can lag a reassignment by up to ReadCacheSeconds.
public sealed record GetDriverDutyStatusQuery(
  Guid DriverId,
  Guid? TruckId = null
) : IRequest<RequestResponse<DriverDutyView>>;

public sealed record DriverDutyView(string? Status, DateTimeOffset? StartedAt)
{
  public static readonly DriverDutyView Unknown = new(null, null);

  // "US" or "CA" from the driver's current forecast, or null.
  public string? Jurisdiction { get; init; }
}

public sealed class GetDriverDutyStatusHandler(
  IAppDbContext db,
  IDriverHosProvider hos,
  IDriverHosStore store,
  IHosHistoryProvider history,
  PlanningSummaryReader summaries,
  TimeProvider clock
) : IRequestHandler<GetDriverDutyStatusQuery, RequestResponse<DriverDutyView>>
{
  public async Task<RequestResponse<DriverDutyView>> Handle(
    GetDriverDutyStatusQuery request,
    CancellationToken ct
  )
  {
    var driver = await db
      .Drivers.AsNoTracking()
      .Where(x => x.Id == request.DriverId)
      .Select(x => new
      {
        x.ExternalId,
        DrivesTruck = request.TruckId.HasValue
          && db.Trucks.Any(t =>
            t.Id == request.TruckId.Value && t.DriverId == x.Id
          ),
      })
      .SingleOrDefaultAsync(ct);
    if (string.IsNullOrEmpty(driver?.ExternalId))
      return RequestResponse<DriverDutyView>.Ok(DriverDutyView.Unknown);
    var external = driver.ExternalId;
    var clocks = await hos.GetClocksAsync(ct);
    if (clocks.Count == 0)
      clocks = await store.ReadAsync(ct);
    var now = clock.GetUtcNow();
    var status = HosDutyStatus.Read(
      await history.GetAsync(external, ct),
      clocks.GetValueOrDefault(external),
      now,
      driver.DrivesTruck && request.TruckId is { } truck
        ? Current(
          (await summaries.ForTruckAsync(truck, ct, summaryOnly: true))
            .State
            ?.Eta,
          now
        )
        : null
    );
    return RequestResponse<DriverDutyView>.Ok(
      status is null
        ? DriverDutyView.Unknown
        : new(status.Status, status.StatusStartedAt)
        {
          Jurisdiction = status.Jurisdiction,
        }
    );
  }

  // A retained forecast past its own validity says where the truck was,
  // not which rules apply now.
  private static string? Current(DispatchEta? eta, DateTimeOffset now) =>
    eta is { ValidUntil: var until } && until >= now.UtcDateTime
      ? eta.DutyStatus?.Jurisdiction
      : null;
}
