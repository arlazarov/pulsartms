using Application.Features.Eta.Services;
using Application.Models;

namespace Application.Features.Eta.Queries;

// When a driver's current duty status began, for a page that shows how
// long they have been in it (DriverDutyReader owns the rule).
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

public sealed class GetDriverDutyStatusHandler(DriverDutyReader duty)
  : IRequestHandler<GetDriverDutyStatusQuery, RequestResponse<DriverDutyView>>
{
  public async Task<RequestResponse<DriverDutyView>> Handle(
    GetDriverDutyStatusQuery request,
    CancellationToken ct
  )
  {
    var status = await duty.ReadAsync(request.DriverId, request.TruckId, ct);
    return RequestResponse<DriverDutyView>.Ok(
      status is null
        ? DriverDutyView.Unknown
        : new(status.Status, status.StatusStartedAt)
        {
          Jurisdiction = status.Jurisdiction,
        }
    );
  }
}
