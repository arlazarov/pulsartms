namespace Domain.Rules.Fleet;

// The number a driver's WhatsApp messages go to: their own WhatsApp number
// when one is set, otherwise their phone. Either must already be a valid
// E.164 number, as contacts are stored. An explicit WhatsApp number that
// is not valid is reported as invalid and never replaced by the phone,
// which would send to a number nobody chose. A phone is only an address
// to try: nothing here knows whether it is registered on WhatsApp.
public static class DriverWhatsApp
{
  public static DriverWhatsAppRecipient Resolve(string? whatsApp, string? phone)
  {
    if (!string.IsNullOrWhiteSpace(whatsApp))
      return ContactAddresses.Phone(whatsApp) == whatsApp
        ? new(whatsApp, DriverWhatsAppSources.WhatsApp)
        : new(null, DriverWhatsAppSources.InvalidWhatsApp);
    return phone is not null && ContactAddresses.Phone(phone) == phone
      ? new(phone, DriverWhatsAppSources.Phone)
      : new(null, DriverWhatsAppSources.None);
  }
}

public sealed record DriverWhatsAppRecipient(string? Number, string Source)
{
  public bool IsUsable => Number is not null;
}

public static class DriverWhatsAppSources
{
  public const string WhatsApp = "whatsapp";
  public const string Phone = "phone";
  public const string InvalidWhatsApp = "invalidWhatsApp";
  public const string None = "none";
}
