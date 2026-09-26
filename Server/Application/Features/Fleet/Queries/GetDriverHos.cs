using Application.Features.Fleet.Services;
using Application.Models;

namespace Application.Features.Fleet.Queries;

// One driver's hours of service, from the same shared snapshot the fleet
// reads (GetFleetHosQuery): never a provider call per viewer. Keyed by the
// driver rather than a truck, so a co-driver is not shown the truck
// driver's clocks. Known is false when the provider holds nothing for
// this driver; UpdatedAt says how old the clocks are.
public sealed record GetDriverHosQuery(Guid DriverId)
  : IRequest<RequestResponse<DriverHoursView>>;

public sealed record DriverHoursView(
  bool Known,
  long? BreakMs,
  long? DriveMs,
  long? ShiftMs,
  long? CycleMs,
  DateTime? UpdatedAt,
  string? DutyStatus
)
{
  public static readonly DriverHoursView Unknown = new(
    false,
    null,
    null,
    null,
    null,
    null,
    null
  );
}

public sealed class GetDriverHosHandler(DriverClockReader reader)
  : IRequestHandler<GetDriverHosQuery, RequestResponse<DriverHoursView>>
{
  public async Task<RequestResponse<DriverHoursView>> Handle(
    GetDriverHosQuery request,
    CancellationToken ct
  )
  {
    if (await reader.DriverAsync(request.DriverId, ct) is not { } driver)
      return RequestResponse<DriverHoursView>.Ok(DriverHoursView.Unknown);
    var clocks = await reader.ClocksAsync(ct);
    return RequestResponse<DriverHoursView>.Ok(
      clocks.GetValueOrDefault(driver.ExternalId) is { } found
        ? new(
          true,
          found.BreakMs,
          found.DriveMs,
          found.ShiftMs,
          found.CycleMs,
          found.UpdatedAt,
          found.CurrentDutyStatus
        )
        : DriverHoursView.Unknown
    );
  }
}
