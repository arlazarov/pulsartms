using Domain.Entities.Fleet;
using Domain.Models.Fleet;
using Domain.Rules.Fleet;

namespace Application.Features.Fleet.Services;

internal static class DriverContacts
{
  // Contact details are personal data: only an active administrator or
  // dispatcher of the carrier that owns the driver reads or changes them.
  public static async Task<bool> MayUseAsync(
    IAppDbContext db,
    ICurrentUser caller,
    IUserRoleService roles,
    CancellationToken ct
  ) =>
    caller.IsAuthenticated
    && !string.IsNullOrEmpty(caller.IdentityUserId)
    && await roles.GetAsync(caller.IdentityUserId, ct) is "Admin" or "Dispatch"
    && await db
      .Users.AsNoTracking()
      .AnyAsync(
        x => x.IdentityUserId == caller.IdentityUserId && x.IsActive,
        ct
      );

  public static DriverContactState State(Driver driver) =>
    new(
      driver.Id,
      driver.Name,
      driver.ContactRevision,
      new(
        driver.Phone,
        driver.ImportedPhone,
        driver.PhoneIsLocal,
        driver.Phone is { } phone && ContactAddresses.Phone(phone) == phone
      ),
      new(
        driver.Email,
        driver.ImportedEmail,
        driver.EmailIsLocal,
        driver.Email is { } email && ContactAddresses.Email(email) == email
      ),
      driver.WhatsAppPhone,
      driver.ContactChangedAt
    );
}
