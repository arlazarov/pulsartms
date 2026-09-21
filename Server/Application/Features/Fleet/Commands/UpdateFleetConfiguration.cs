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
) : IRequest<RequestResponse<FleetConfigurationState>>, IChecked
{
  public IEnumerable<string> Wrong()
  {
    if (!FleetConfigurationAccess.ValidKind(Kind))
      yield return "Choose trucks, trailers or drivers.";
    if (Id == Guid.Empty)
      yield return "Choose what to change.";
    if (Update is null)
      yield return "The change to save is missing.";
    else
    {
      if (Update.Revision is < 0 or long.MaxValue)
        yield return "Reopen this before saving it.";
      if (
        string.IsNullOrWhiteSpace(Update.Name)
        || Update.Name.Length > 200
        || Update.Name.Any(char.IsControl)
      )
        yield return "Enter a name of at most 200 ordinary characters.";
      if (Update.Vin is null || Update.Vin.Length > 17)
        yield return "A VIN is at most 17 characters.";
      else if (!Update.Vin.All(char.IsAsciiLetterOrDigit))
        yield return "VIN may contain only letters and digits.";
      if (
        Update.FuelCard is null
        || Update.FuelCard.Length > 50
        || Update.FuelCard.Any(char.IsControl)
      )
        yield return "A fuel card is at most 50 ordinary characters.";
    }
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
