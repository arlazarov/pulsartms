using Application.Features.Eta.Interfaces;
using Application.Features.Fleet.Interfaces;
using Application.Models;
using Domain.Rules.Eta;

namespace Application.Features.Eta.Queries;

// When a driver's current duty status began, for a page that shows how
// long they have been in it, under the rule the forecasts use. The history
// is the forecasts' own: read through the provider's gate and one-minute
// cache, so opening a conversation costs at most one history read per
// driver a minute, and none when a forecast read it lately. StartedAt is
// null when that history is unavailable, stale or disagrees with the
// clocks - never the time the clocks were fetched.
public sealed record GetDriverDutyStatusQuery(Guid DriverId)
  : IRequest<RequestResponse<DriverDutyView>>;

public sealed record DriverDutyView(string? Status, DateTimeOffset? StartedAt)
{
  public static readonly DriverDutyView Unknown = new(null, null);
}

public sealed class GetDriverDutyStatusHandler(
  IAppDbContext db,
  IDriverHosProvider hos,
  IDriverHosStore store,
  IHosHistoryProvider history,
  TimeProvider clock
) : IRequestHandler<GetDriverDutyStatusQuery, RequestResponse<DriverDutyView>>
{
  public async Task<RequestResponse<DriverDutyView>> Handle(
    GetDriverDutyStatusQuery request,
    CancellationToken ct
  )
  {
    var external = await db
      .Drivers.AsNoTracking()
      .Where(x => x.Id == request.DriverId)
      .Select(x => x.ExternalId)
      .SingleOrDefaultAsync(ct);
    if (string.IsNullOrEmpty(external))
      return RequestResponse<DriverDutyView>.Ok(DriverDutyView.Unknown);
    var clocks = await hos.GetClocksAsync(ct);
    if (clocks.Count == 0)
      clocks = await store.ReadAsync(ct);
    var status = HosDutyStatus.Read(
      await history.GetAsync(external, ct),
      clocks.GetValueOrDefault(external),
      clock.GetUtcNow()
    );
    return RequestResponse<DriverDutyView>.Ok(
      status is null
        ? DriverDutyView.Unknown
        : new(status.Status, status.StatusStartedAt)
    );
  }
}
