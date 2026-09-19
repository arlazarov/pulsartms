using Application.Features.Users.Models;
using Application.Models;

namespace Application.Features.Users.Queries;

public sealed record GetAppearanceSettingsQuery
  : IRequest<RequestResponse<AppearanceSettings>>;

public sealed class GetAppearanceSettingsHandler(
  IAppDbContext db,
  ICurrentUser currentUser
)
  : IRequestHandler<
    GetAppearanceSettingsQuery,
    RequestResponse<AppearanceSettings>
  >
{
  public async Task<RequestResponse<AppearanceSettings>> Handle(
    GetAppearanceSettingsQuery request,
    CancellationToken cancellationToken
  )
  {
    var identity = currentUser.IdentityUserId;
    if (!currentUser.IsAuthenticated || string.IsNullOrWhiteSpace(identity))
      return RequestResponse<AppearanceSettings>.Fail("Unauthorized.", 401);

    var settings = await db
      .Users.AsNoTracking()
      .Where(x => x.IdentityUserId == identity && x.IsActive)
      .Select(x => new AppearanceSettings(
        x.Theme,
        x.TemperatureUnit,
        x.DistanceUnit
      ))
      .SingleOrDefaultAsync(cancellationToken);
    return settings is null
      ? RequestResponse<AppearanceSettings>.Fail("Unauthorized.", 401)
      : RequestResponse<AppearanceSettings>.Ok(settings);
  }
}
