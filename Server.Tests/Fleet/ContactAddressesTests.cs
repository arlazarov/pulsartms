using Domain.Rules.Fleet;

namespace Server.Tests.Fleet;

// A number is completed only when its country is certain; anything else
// must carry its own "+" and country code.
[Trait("Category", "Fleet")]
[Trait("Kind", "Unit")]
public sealed class ContactAddressesTests
{
  [Theory]
  [InlineData("5558234327", "+15558234327")]
  [InlineData("(555) 823-4327", "+15558234327")]
  [InlineData("1 555 823 4327", "+15558234327")]
  [InlineData("+1 555-823-4327", "+15558234327")]
  [InlineData("+44 20 7946 0958", "+442079460958")]
  [InlineData("+380 44 123 4567", "+380441234567")]
  public void CompletesOnlyCertainNumbers(string input, string expected) =>
    Assert.Equal(expected, ContactAddresses.Phone(input));

  [Theory]
  [InlineData("")]
  [InlineData("   ")]
  [InlineData("0558234327")]
  [InlineData("5550234327")]
  [InlineData("823-4327")]
  [InlineData("442079460958")]
  [InlineData("+0 555 823 4327")]
  [InlineData("+1234567")]
  [InlineData("+1234567890123456")]
  [InlineData("5558234327 x12")]
  [InlineData("00442079460958")]
  [InlineData("+1 555 823 4327\u0007")]
  public void RefusesNumbersWhoseCountryOrShapeIsUncertain(string input) =>
    Assert.Null(ContactAddresses.Phone(input));

  [Theory]
  [InlineData("driver@example.com", "driver@example.com")]
  [InlineData(
    "  d.river+fuel@mail.example.org ",
    "d.river+fuel@mail.example.org"
  )]
  public void KeepsAPlainEmailAddress(string input, string expected) =>
    Assert.Equal(expected, ContactAddresses.Email(input));

  [Theory]
  [InlineData("driver")]
  [InlineData("driver@localhost")]
  [InlineData("Driver <driver@example.com>")]
  [InlineData("driver@example.com, other@example.com")]
  [InlineData("dri ver@example.com")]
  [InlineData("driver@example.")]
  public void RefusesAnythingButOneAddress(string input) =>
    Assert.Null(ContactAddresses.Email(input));
}
