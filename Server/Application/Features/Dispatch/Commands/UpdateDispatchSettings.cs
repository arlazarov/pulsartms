using Application.Features.Dispatch.Models;
using Application.Models;
using Domain.Entities.Dispatch;

namespace Application.Features.Dispatch.Commands;

public sealed record UpdateDispatchSettingsCommand(
  string? LoadNumberPrefix,
  long Revision,
  string? TemperatureUnit = null,
  string? DistanceUnit = null
) : IRequest<RequestResponse<DispatchSettingsState>>;

public sealed class UpdateDispatchSettingsValidator
  : AbstractValidator<UpdateDispatchSettingsCommand>
{
  public UpdateDispatchSettingsValidator()
  {
    RuleFor(x => x.LoadNumberPrefix)
      .Cascade(CascadeMode.Stop)
      .NotNull()
      .Must(prefix => prefix!.Trim().Length <= 16)
      .WithMessage("Load number prefix must be at most 16 characters.")
      .Must(prefix => !prefix!.Any(char.IsControl))
      .WithMessage("Load number prefix cannot contain control characters.");
    RuleFor(x => x.Revision).GreaterThanOrEqualTo(0).LessThan(long.MaxValue);
    RuleFor(x => x.TemperatureUnit)
      .Must(value => value is null or "fahrenheit" or "celsius" or "both")
      .WithMessage("Choose Fahrenheit, Celsius or both.");
    RuleFor(x => x.DistanceUnit)
      .Must(value => value is null or "miles" or "kilometers" or "both")
      .WithMessage("Choose miles, kilometers or both.");
  }
}

public sealed class UpdateDispatchSettingsHandler(
  IAppDbContext db,
  TimeProvider clock
)
  : IRequestHandler<
    UpdateDispatchSettingsCommand,
    RequestResponse<DispatchSettingsState>
  >
{
  public async Task<RequestResponse<DispatchSettingsState>> Handle(
    UpdateDispatchSettingsCommand request,
    CancellationToken cancellationToken
  )
  {
    var entity = await db.DispatchSettings.SingleOrDefaultAsync(
      x => x.Id == DispatchSettings.SingletonId,
      cancellationToken
    );
    if (request.Revision != (entity?.Revision ?? 0))
      return Conflict();
    var prefix = request.LoadNumberPrefix!.Trim();
    // Older clients can still update the prefix without resetting the saved
    // units.
    var temperature =
      request.TemperatureUnit ?? entity?.TemperatureUnit ?? "both";
    var distance = request.DistanceUnit ?? entity?.DistanceUnit ?? "both";
    if (
      entity is not null
      && entity.LoadNumberPrefix == prefix
      && entity.TemperatureUnit == temperature
      && entity.DistanceUnit == distance
    )
      return RequestResponse<DispatchSettingsState>.Ok(
        new(prefix, entity.Revision, entity.UpdatedAt, temperature, distance)
      );
    var creating = entity is null;
    if (entity is null)
    {
      entity = new() { Id = DispatchSettings.SingletonId };
      db.DispatchSettings.Add(entity);
    }
    entity.LoadNumberPrefix = prefix;
    entity.TemperatureUnit = temperature;
    entity.DistanceUnit = distance;
    entity.Revision++;
    entity.UpdatedAt = clock.GetUtcNow().UtcDateTime;
    try
    {
      await db.SaveChangesAsync(cancellationToken);
    }
    catch (DbUpdateConcurrencyException)
    {
      db.Entry(entity).State = EntityState.Detached;
      return Conflict();
    }
    catch (DbUpdateException) when (creating)
    {
      db.Entry(entity).State = EntityState.Detached;
      if (
        await db
          .DispatchSettings.AsNoTracking()
          .AnyAsync(
            x => x.Id == DispatchSettings.SingletonId,
            cancellationToken
          )
      )
        return Conflict();
      throw;
    }
    return RequestResponse<DispatchSettingsState>.Ok(
      new(prefix, entity.Revision, entity.UpdatedAt, temperature, distance)
    );
  }

  private static RequestResponse<DispatchSettingsState> Conflict() =>
    RequestResponse<DispatchSettingsState>.Fail(
      "Dispatch settings were changed in another session. Reload the latest settings before saving.",
      409
    );
}
