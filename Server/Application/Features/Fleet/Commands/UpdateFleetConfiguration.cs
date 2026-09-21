using System.Data;
using Application.Caching;
using Application.Features.Fleet.Queries.GetFleetLocations;
using Application.Features.Fleet.Services;
using Application.Models;
using Domain.Entities.Fleet;
using Domain.Models.Fleet;
using Microsoft.Extensions.Caching.Memory;

namespace Application.Features.Fleet.Commands;

public sealed record UpdateFleetConfigurationCommand(
  string Kind,
  Guid Id,
  FleetConfigurationUpdate Update
) : IRequest<RequestResponse<FleetConfigurationState>>;

public sealed class UpdateFleetConfigurationValidator
  : AbstractValidator<UpdateFleetConfigurationCommand>
{
  public UpdateFleetConfigurationValidator()
  {
    RuleFor(x => x.Kind).Must(FleetConfigurationAccess.ValidKind);
    RuleFor(x => x.Id).NotEmpty();
    RuleFor(x => x.Update).NotNull();
    When(
      x => x.Update is not null,
      () =>
      {
        RuleFor(x => x.Update.Revision).InclusiveBetween(0, long.MaxValue - 1);
        RuleFor(x => x.Update.Name)
          .NotEmpty()
          .MaximumLength(200)
          .Must(x => x is not null && !x.Any(char.IsControl));
        RuleFor(x => x.Update.Vin)
          .NotNull()
          .MaximumLength(17)
          .Must(x => x is not null && x.All(char.IsAsciiLetterOrDigit))
          .WithMessage("VIN may contain only letters and digits.");
        RuleFor(x => x.Update.FuelCard)
          .NotNull()
          .MaximumLength(50)
          .Must(x => x is not null && !x.Any(char.IsControl));
      }
    );
  }
}

public sealed class UpdateFleetConfigurationHandler(
  IAppDbContext db,
  ICurrentUser caller,
  IUserRoleService roles,
  TimeProvider clock,
  ReadCache reads,
  IMemoryCache memory
)
  : IRequestHandler<
    UpdateFleetConfigurationCommand,
    RequestResponse<FleetConfigurationState>
  >
{
  public async Task<RequestResponse<FleetConfigurationState>> Handle(
    UpdateFleetConfigurationCommand request,
    CancellationToken ct
  )
  {
    if (!await FleetConfigurationAccess.IsAdminAsync(db, caller, roles, ct))
      return Fail("Administrator access is required.", 403);
    await using var transaction = await db.Database.BeginTransactionAsync(
      IsolationLevel.Serializable,
      ct
    );
    var resource = await FleetConfigurationReader.FindAsync(
      db,
      request.Kind,
      request.Id,
      ct
    );
    if (resource is null)
      return Fail("Fleet resource not found.", 404);
    var update = request.Update;
    if (resource.ConfigurationRevision != update.Revision)
      return Conflict();
    if (
      resource is Truck truck && update.Name != truck.UnitNumber
      || resource is Trailer trailer && update.Name != trailer.UnitNumber
    )
      return Fail(
        "Imported unit numbers are source-owned. Change the unit at source."
      );
    var active = update.UseImported
      ? resource.ImportedIsActive ?? resource.IsActive
      : update.IsActive;
    if (!active && resource.IsActive)
    {
      if (
        await FleetConfigurationReader.References(db, resource).AnyAsync(ct)
        || await FleetConfigurationReader.HasExecutionAsync(db, resource, ct)
        || (
          await FleetConfigurationReader.AssignmentsAsync(db, resource, ct)
        ).Length > 0
      )
        return Fail(
          "This resource still has fleet or unfinished dispatch assignments. "
            + "Resolve those assignments before making it inactive.",
          409
        );
    }
    Apply(resource, update);
    resource.IsActive = active;
    resource.IsLocallyConfigured = !update.UseImported;
    resource.ConfigurationRevision++;
    resource.ConfiguredAt = clock.GetUtcNow().UtcDateTime;
    resource.ConfiguredBy = caller.IdentityUserId;
    try
    {
      await db.SaveChangesAsync(ct);
      await transaction.CommitAsync(ct);
    }
    catch (DbUpdateConcurrencyException)
    {
      db.Entry(resource).State = EntityState.Detached;
      return Conflict();
    }
    reads.Invalidate("fleet-catalog");
    reads.Invalidate("board");
    reads.Invalidate("route-previews");
    memory.Remove(FleetCache.CacheKey);
    memory.Remove("fleet-driver-ids");
    memory.Remove("assignment-sync-signature");
    memory.Remove("dispatch-sync-signature");
    return RequestResponse<FleetConfigurationState>.Ok(
      await FleetConfigurationReader.StateAsync(db, resource, ct)
    );
  }

  private static void Apply(
    IFleetConfiguration resource,
    FleetConfigurationUpdate update
  )
  {
    resource.ImportedIsActive ??= resource.IsActive;
    switch (resource)
    {
      case Truck truck:
        truck.ImportedVin ??= truck.Vin;
        truck.Vin = update.UseImported
          ? truck.ImportedVin
          : update.Vin.Trim().ToUpperInvariant();
        break;
      case Trailer trailer:
        trailer.ImportedVin ??= trailer.Vin;
        trailer.Vin = update.UseImported
          ? trailer.ImportedVin
          : update.Vin.Trim().ToUpperInvariant();
        break;
      case Driver driver:
        driver.ImportedName ??= driver.Name;
        driver.ImportedFuelCard ??= driver.FuelCard;
        driver.Name = update.UseImported
          ? driver.ImportedName
          : update.Name.Trim();
        driver.FuelCard = update.UseImported
          ? driver.ImportedFuelCard
          : update.FuelCard.Trim();
        break;
    }
  }

  private static RequestResponse<FleetConfigurationState> Conflict() =>
    Fail(
      "This resource changed in another session or during import. "
        + "Reload it before saving.",
      409
    );

  private static RequestResponse<FleetConfigurationState> Fail(
    string message,
    int status = 400
  ) => RequestResponse<FleetConfigurationState>.Fail(message, status);
}
