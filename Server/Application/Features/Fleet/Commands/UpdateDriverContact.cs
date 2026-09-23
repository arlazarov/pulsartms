using Application.Features.Fleet.Services;
using Application.Models;
using Domain.Entities.Fleet;
using Domain.Models.Fleet;
using Domain.Rules.Fleet;

namespace Application.Features.Fleet.Commands;

public sealed record UpdateDriverContactCommand(
  Guid DriverId,
  DriverContactUpdate Update
) : IRequest<RequestResponse<DriverContactState>>, IChecked
{
  public const string PhoneHint =
    "with + and country code, or as a 10-digit US or Canada number.";

  public IEnumerable<string> Wrong()
  {
    if (DriverId == Guid.Empty)
      yield return "Choose a driver.";
    if (Update is null)
    {
      yield return "The change to save is missing.";
      yield break;
    }
    if (Update.Revision is < 0 or long.MaxValue)
      yield return "Reopen this driver before saving.";
    if (
      !Update.PhoneFromSource
      && !string.IsNullOrWhiteSpace(Update.Phone)
      && ContactAddresses.Phone(Update.Phone) is null
    )
      yield return "Enter the phone " + PhoneHint;
    if (
      !Update.EmailFromSource
      && !string.IsNullOrWhiteSpace(Update.Email)
      && ContactAddresses.Email(Update.Email) is null
    )
      yield return "Enter a valid email address.";
    if (
      !string.IsNullOrWhiteSpace(Update.WhatsAppPhone)
      && ContactAddresses.Phone(Update.WhatsAppPhone) is null
    )
      yield return "Enter the WhatsApp number " + PhoneHint;
  }
}

public sealed class UpdateDriverContactHandler(
  IAppDbContext db,
  ICurrentUser caller,
  IUserRoleService roles,
  TimeProvider clock
)
  : IRequestHandler<
    UpdateDriverContactCommand,
    RequestResponse<DriverContactState>
  >
{
  public async Task<RequestResponse<DriverContactState>> Handle(
    UpdateDriverContactCommand request,
    CancellationToken ct
  )
  {
    if (!await DriverContacts.MayUseAsync(db, caller, roles, ct))
      return Fail("You cannot change driver contacts.", 403);
    var driver = await db.Drivers.SingleOrDefaultAsync(
      x => x.Id == request.DriverId,
      ct
    );
    if (driver is null)
      return Fail("Driver not found.", 404);
    var update = request.Update;
    if (driver.ContactRevision != update.Revision)
      return Conflict();
    Apply(driver, update);
    driver.ContactRevision++;
    driver.ContactChangedAt = clock.GetUtcNow().UtcDateTime;
    driver.ContactChangedBy = caller.IdentityUserId;
    try
    {
      await db.SaveChangesAsync(ct);
    }
    catch (DbUpdateConcurrencyException)
    {
      db.Entry(driver).State = EntityState.Detached;
      return Conflict();
    }
    return RequestResponse<DriverContactState>.Ok(DriverContacts.State(driver));
  }

  public static void Apply(Driver driver, DriverContactUpdate update)
  {
    driver.PhoneIsLocal = !update.PhoneFromSource;
    driver.Phone = update.PhoneFromSource
      ? DriverContactImport.SourcePhone(driver.ImportedPhone)
      : ContactAddresses.Phone(update.Phone);
    driver.EmailIsLocal = !update.EmailFromSource;
    driver.Email = update.EmailFromSource
      ? driver.ImportedEmail
      : ContactAddresses.Email(update.Email);
    driver.WhatsAppPhone = ContactAddresses.Phone(update.WhatsAppPhone);
  }

  private static RequestResponse<DriverContactState> Conflict() =>
    Fail(
      "This driver's contacts changed in another session or during import. "
        + "Reload them before saving.",
      409
    );

  private static RequestResponse<DriverContactState> Fail(
    string message,
    int status = 400
  ) => RequestResponse<DriverContactState>.Fail(message, status);
}
