using Application.Features.Users.Models;
using Application.Models;

namespace Application.Features.Users.Commands;

public sealed record UpdateAppearanceSettingsCommand(
  string Theme,
  string? TemperatureUnit = null,
  string? DistanceUnit = null
) : IRequest<RequestResponse<AppearanceSettings>>;

public sealed class UpdateAppearanceSettingsValidator
  : AbstractValidator<UpdateAppearanceSettingsCommand>
{
  public UpdateAppearanceSettingsValidator()
  {
    RuleFor(x => x.Theme)
      .Must(x => x is "light" or "dark")
      .WithMessage("Choose the light or dark theme.");
    RuleFor(x => x.TemperatureUnit)
      .Must(x => x is null or "fahrenheit" or "celsius" or "both")
      .WithMessage("Choose Fahrenheit, Celsius or both.");
    RuleFor(x => x.DistanceUnit)
      .Must(x => x is null or "miles" or "kilometers" or "both")
      .WithMessage("Choose miles, kilometers or both.");
  }
}

public sealed class UpdateAppearanceSettingsHandler(
  IAppDbContext db,
  ICurrentUser currentUser
)
  : IRequestHandler<
    UpdateAppearanceSettingsCommand,
    RequestResponse<AppearanceSettings>
  >
{
  public async Task<RequestResponse<AppearanceSettings>> Handle(
    UpdateAppearanceSettingsCommand request,
    CancellationToken cancellationToken
  )
  {
    var identity = currentUser.IdentityUserId;
    if (!currentUser.IsAuthenticated || string.IsNullOrWhiteSpace(identity))
      return RequestResponse<AppearanceSettings>.Fail("Unauthorized.", 401);
    if (request.Theme is not ("light" or "dark"))
      return RequestResponse<AppearanceSettings>.Fail(
        "Choose the light or dark theme."
      );
    if (
      request.TemperatureUnit
        is not (null or "fahrenheit" or "celsius" or "both")
      || request.DistanceUnit is not (null or "miles" or "kilometers" or "both")
    )
      return RequestResponse<AppearanceSettings>.Fail(
        "Choose valid temperature and distance units."
      );

    var user = await db.Users.SingleOrDefaultAsync(
      x => x.IdentityUserId == identity && x.IsActive,
      cancellationToken
    );
    if (user is null)
      return RequestResponse<AppearanceSettings>.Fail("Unauthorized.", 401);

    user.Theme = request.Theme;
    if (request.TemperatureUnit is { } temperature)
      user.TemperatureUnit = temperature;
    if (request.DistanceUnit is { } distance)
      user.DistanceUnit = distance;
    await db.SaveChangesAsync(cancellationToken);
    return RequestResponse<AppearanceSettings>.Ok(
      new(user.Theme, user.TemperatureUnit, user.DistanceUnit)
    );
  }
}
