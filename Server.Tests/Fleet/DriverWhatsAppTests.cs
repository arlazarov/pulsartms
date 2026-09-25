using Domain.Rules.Fleet;

namespace Server.Tests.Fleet;

// Where a driver's WhatsApp messages go: their own WhatsApp number when
// set, otherwise their phone; an explicit number that is not valid is
// reported, never replaced by the phone.
[Trait("Category", "Messaging")]
[Trait("Kind", "Unit")]
public sealed class DriverWhatsAppTests
{
  [Theory]
  [InlineData("+15550000001", "+15550000002", "+15550000001", "whatsapp")]
  [InlineData(null, "+15550000002", "+15550000002", "phone")]
  [InlineData("", "+15550000002", "+15550000002", "phone")]
  [InlineData("  ", "+15550000002", "+15550000002", "phone")]
  [InlineData("555-0001", "+15550000002", null, "invalidWhatsApp")]
  [InlineData(null, "(555) 000-0002", null, "none")]
  [InlineData(null, null, null, "none")]
  public void TheWhatsAppNumberWinsAndThePhoneFillsIn(
    string? whatsApp,
    string? phone,
    string? number,
    string source
  )
  {
    var recipient = DriverWhatsApp.Resolve(whatsApp, phone);

    Assert.Equal((number, source), (recipient.Number, recipient.Source));
    Assert.Equal(number is not null, recipient.IsUsable);
  }
}
