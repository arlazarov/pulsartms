using Application.Features.Fuel.Queries.GetFuelStations;
using Domain.Models.Fuel;
using Domain.Rules.Routing;

namespace Server.Tests.Fuel;

[Trait("Category", "Fuel")]
[Trait("Kind", "Unit")]
public sealed class UsFuelDiscountSignatureTests
{
  private static readonly DateOnly Today = new(2026, 9, 14);

  [Fact]
  public void RepeatedPricesCanadianPricesAndFutureQuotesDoNotTriggerRefresh()
  {
    var station = Station();
    var original = UsFuelDiscountSignature.From([station], Today);
    var quote = station.Discounts.Single();
    var updated = station with
    {
      Discounts =
      [
        quote with
        {
          Currency = "CAD",
          Unit = "L",
        },
        quote with
        {
          DiscountPrice = 2,
          EffectiveFrom = Today.AddDays(1),
        },
        quote with
        {
          Product = "Reefer Diesel",
        },
        quote,
        quote,
      ],
    };
    Assert.Equal(original, UsFuelDiscountSignature.From([updated], Today));
    Assert.NotEqual(
      original,
      UsFuelDiscountSignature.From([updated], Today.AddDays(1))
    );
    Assert.NotEqual(
      original,
      UsFuelDiscountSignature.From(
        [station with { Discounts = [quote with { DiscountPrice = 3.1m }] }],
        Today
      )
    );
  }

  [Fact]
  public void StationOrderingDoesNotChangeSignature()
  {
    var a = Station();
    var b = Station();
    Assert.Equal(
      UsFuelDiscountSignature.From([a, b], Today),
      UsFuelDiscountSignature.From([b, a], Today)
    );
  }

  internal static FuelStationDto Station() =>
    new(
      Guid.NewGuid(),
      "US-1",
      "Station",
      "",
      "",
      "VA",
      "",
      "US",
      40,
      -75,
      [new("USD", "Diesel", 4, 3, 1, Today, Today.AddDays(2), null, "US gal")]
    );
}
