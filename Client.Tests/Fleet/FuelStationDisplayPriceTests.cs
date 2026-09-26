using Bunit;
using Client.Models.DTO.Fleet;
using Client.Pages.FleetMap;

namespace Client.Tests.Fleet;

// The API selects the eligible cash and IFTA quotes for the requested day.
// The list chooses only the requested basis; stale raw history cannot replace
// the server's selection.
[Trait("Category", "Fleet")]
[Trait("Kind", "Component")]
public sealed class FuelStationDisplayPriceTests
{
  [Theory]
  [InlineData(false, "3.250 USD / gal")]
  [InlineData(true, "3.100 USD / gal")]
  public void TheListFormatsTheServersSelectedQuote(bool ifta, string expected)
  {
    using var context = new BunitContext();
    var station = new FuelStationMapDto
    {
      Id = Guid.NewGuid(),
      Name = "Selected station",
      City = "Rome",
      Region = "NY",
      CashDiscount = Quote(3.250m, 3.200m),
      IftaDiscount = Quote(3.150m, 3.100m),
      Discounts = [Quote(1.000m, 0.900m)],
    };

    var component = context.Render<FleetFuelStations>(parameters =>
      parameters
        .Add(x => x.Stations, [station])
        .Add(x => x.Date, new DateOnly(2026, 9, 26))
        .Add(x => x.UseIfta, ifta)
    );

    Assert.Equal(
      expected,
      component.Find(".fleet-fuel-stations__price").TextContent
    );
  }

  private static FuelDiscountMapDto Quote(decimal cash, decimal ifta) =>
    new()
    {
      Currency = "USD",
      Unit = "gal",
      DiscountPrice = cash,
      PriceAfterIfta = ifta,
    };
}
