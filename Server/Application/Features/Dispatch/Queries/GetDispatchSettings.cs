using Application.Features.Dispatch.Models;
using Application.Models;
using Domain.Entities.Dispatch;

namespace Application.Features.Dispatch.Queries;

public sealed record GetDispatchSettingsQuery
  : IRequest<RequestResponse<DispatchSettingsState>>;

public sealed class GetDispatchSettingsHandler(IAppDbContext db)
  : IRequestHandler<
    GetDispatchSettingsQuery,
    RequestResponse<DispatchSettingsState>
  >
{
  public async Task<RequestResponse<DispatchSettingsState>> Handle(
    GetDispatchSettingsQuery request,
    CancellationToken cancellationToken
  )
  {
    var saved = await db
      .DispatchSettings.AsNoTracking()
      .Where(x => x.Id == DispatchSettings.SingletonId)
      .Select(x => new DispatchSettingsState(
        x.LoadNumberPrefix,
        x.Revision,
        x.UpdatedAt,
        x.TemperatureUnit,
        x.DistanceUnit,
        x.AutomaticFuelSending
      ))
      .SingleOrDefaultAsync(cancellationToken);
    return RequestResponse<DispatchSettingsState>.Ok(
      saved ?? new("AMF", 0, null)
    );
  }
}
