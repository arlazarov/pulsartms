using Domain.Rules.Fleet;

namespace Domain.Entities.Fleet;

public static class DriverContactImport
{
  // The source copy is always refreshed; the value in use follows it only
  // while nobody here has set or cleared that field. A source phone that is
  // not a complete number is shown as written and never sent to.
  public static void Apply(Driver driver, string? phone, string? email)
  {
    phone = Source(phone, ContactAddresses.MaximumPhoneInput);
    email = Source(email, ContactAddresses.MaximumEmail);
    if (driver.ImportedPhone == phone && driver.ImportedEmail == email)
      return;
    driver.ImportedPhone = phone;
    driver.ImportedEmail = email;
    if (!driver.PhoneIsLocal)
      driver.Phone = SourcePhone(phone);
    if (!driver.EmailIsLocal)
      driver.Email = email;
    driver.ContactRevision++;
  }

  public static string? SourcePhone(string? imported) =>
    ContactAddresses.Phone(imported) ?? imported;

  private static string? Source(string? value, int limit)
  {
    var text = value?.Trim();
    return
      string.IsNullOrEmpty(text)
      || text.Length > limit
      || text.Any(char.IsControl)
      ? null
      : text;
  }
}
