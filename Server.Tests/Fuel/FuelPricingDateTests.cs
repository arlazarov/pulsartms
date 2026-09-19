using Application.Features.Fuel.Models;
using Application.Features.Routing.Algorithms;

namespace Server.Tests.Fuel;

[Trait("Category", "Fuel")]
public class FuelPricingDateTests
{
  [Theory]
  [InlineData("2026-09-06T00:30:00Z", "2026-09-05")]
  [InlineData("2026-09-06T04:00:00Z", "2026-09-06")]
  [InlineData("2026-01-06T04:30:00Z", "2026-01-05")]
  [InlineData("2026-01-06T05:00:00Z", "2026-01-06")]
  public void UsesBusinessDayAcrossUtcMidnightAndDaylightSaving(
    string utc,
    string expected
  )
  {
    Assert.Equal(
      DateOnly.Parse(expected),
      FuelPricingDate.FromUtc(DateTime.Parse(utc).ToUniversalTime())
    );
  }
}
