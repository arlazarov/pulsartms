using Client.Services;

namespace Client.Tests.Fleet;

[Trait("Category", "Fleet")]
[Trait("Kind", "Unit")]
public sealed class StopAddressLinesTests
{
  [Theory]
  [InlineData(
    "530 Henry St, Rome, NY, 13440, US",
    "530 Henry St",
    "Rome, NY 13440, US"
  )]
  [InlineData(
    "530 Henry St, Rome, NY 13440, USA",
    "530 Henry St",
    "Rome, NY 13440, USA"
  )]
  [InlineData(
    "308 Springhill Farm Rd, building 3, Fort Mill, SC, 29715, US",
    "308 Springhill Farm Rd, building 3",
    "Fort Mill, SC 29715, US"
  )]
  [InlineData(
    "101 Main St, Suite 20, Albany, NY12207-1234, United States",
    "101 Main St, Suite 20",
    "Albany, NY 12207-1234, United States"
  )]
  [InlineData(
    "10 Front St, Toronto, ON, M5V 3A8, Canada",
    "10 Front St",
    "Toronto, ON M5V 3A8, Canada"
  )]
  [InlineData(
    "10 Front St, Toronto, ON M5V3A8, CA",
    "10 Front St",
    "Toronto, ON M5V3A8, CA"
  )]
  [InlineData(
    "10 Front St, Toronto, ON M5V 3A8",
    "10 Front St",
    "Toronto, ON M5V 3A8"
  )]
  [InlineData("123 Main St, Los Angeles, CA", "123 Main St", "Los Angeles, CA")]
  [InlineData(
    "123 Main St, Los Angeles, CA, 90210",
    "123 Main St",
    "Los Angeles, CA 90210"
  )]
  [InlineData("123 Main St, Albany, NY", "123 Main St", "Albany, NY")]
  public void RecognizedAddressTailsSeparateLocalityWithoutLosingStreetOrUnit(
    string address,
    string street,
    string locality
  )
  {
    Assert.Equal(
      new StopAddressLines(street, locality),
      StopAddressLines.Create(address)
    );
  }

  [Theory]
  [InlineData("Rome, NY, 13440, US")]
  [InlineData("Rome, NY 13440, US")]
  [InlineData("Toronto, ON M5V3A8, CA")]
  [InlineData("530 Henry St, building 3, NY 13440, US")]
  [InlineData("530 Henry St Rome NY 13440 US")]
  [InlineData("123 Main St, Fort Mill, South Carolina, 29715, US")]
  [InlineData("123 Main St, Unknown City, ZZ 12345, US")]
  [InlineData("123 Main St, 13440, NY, US")]
  public void MissingStreetOrUnclearLocalityDoesNotInventAnAddressSplit(
    string address
  )
  {
    Assert.Equal(
      new StopAddressLines(address, ""),
      StopAddressLines.Create(address)
    );
  }

  [Theory]
  [InlineData(null)]
  [InlineData("")]
  [InlineData(" ,  , ")]
  public void MissingAddressProducesEmptyLines(string? address)
  {
    Assert.Equal(
      new StopAddressLines("", ""),
      StopAddressLines.Create(address)
    );
  }
}
