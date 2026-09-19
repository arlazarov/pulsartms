using System.Globalization;
using Application.Features.Fuel.Queries.GetFuelStations;
using Application.Features.Routing.Algorithms;
using Application.Features.Routing.Models;

namespace Server.Tests.Fuel;

[Trait("Category", "Fuel")]
[Trait("Kind", "Unit")]
public sealed class FuelDisplayPricesTests
{
  private static readonly DateOnly Date = new(2026, 9, 9);

  [Theory]
  [InlineData("USD", "US gal", false)]
  [InlineData("USD", "US gal", true)]
  [InlineData("CAD", "L", false)]
  [InlineData("CAD", "L", true)]
  public void DisplayUsesTheSameEligibleMinimumAsPlanning(
    string currency,
    string unit,
    bool ifta
  )
  {
    var station = Station(
      [
        Quote("DEF", .1m, currency, unit),
        Quote("Reefer Diesel", .2m, currency, unit),
        Quote("Diesel", 3.5m, currency, unit),
        Quote("ULSD Diesel", 3m, currency, unit) with
        {
          EffectiveFrom = Date.AddDays(-1),
        },
        Quote("Diesel", .3m, currency, unit) with
        {
          EffectiveTo = Date.AddDays(-1),
        },
        Quote("Diesel", .4m, currency, unit) with
        {
          EffectiveFrom = Date.AddDays(1),
        },
      ]
    );
    var selected = FuelDisplayPrices.Select(station, Date);
    var display = ifta ? selected.IftaDiscount : selected.CashDiscount;
    var planning = Assert.Single(
      FuelRegionGrid.Prices(
        [station],
        new TruckRouteProfile { UseIfta = ifta, CadToUsd = .75 },
        Date
      )
    );
    Assert.NotNull(display);
    Assert.Equal("ULSD Diesel", display.Product);
    Assert.Equal((double)display.DiscountPrice, planning.Station.YourPrice);
    Assert.Equal(
      (double)(ifta ? display.PriceAfterIfta!.Value : display.DiscountPrice),
      planning.Station.EconomicPrice
    );
    Assert.Equal(unit, display.Unit);
    Assert.Same(station.Discounts, selected.Discounts);
  }

  [Fact]
  public void IftaSelectionUsesReadyNetValuesAndDoesNotInferMissingTax()
  {
    var cash = Quote("Diesel", 2m) with { PriceAfterIfta = null };
    var net = Quote("Diesel", 3m) with { PriceAfterIfta = 1.5m };
    var selected = FuelDisplayPrices.Select(Station([cash, net]), Date);
    Assert.Same(cash, selected.CashDiscount);
    Assert.Same(net, selected.IftaDiscount);
    Assert.Null(FuelDisplayPrices.Select(Station([cash]), Date).IftaDiscount);
  }

  [Fact]
  public void MixedCurrenciesKeepTheStationButDoNotInventAnExchangeRate()
  {
    var station = Station(
      [Quote("Diesel", 3m), Quote("Diesel", 1m, "CAD", "L")]
    );
    var selected = FuelDisplayPrices.Select(station, Date);
    Assert.Null(selected.CashDiscount);
    Assert.Null(selected.IftaDiscount);
    Assert.Equal(station.Latitude, selected.Latitude);
    Assert.Same(station.Discounts, selected.Discounts);
  }

  [Theory]
  [InlineData("USD", "L", "Diesel", "1")]
  [InlineData("CAD", "US gal", "Diesel", "1")]
  [InlineData("EUR", "L", "Diesel", "1")]
  [InlineData("USD", "US gal", "DEF", "1")]
  [InlineData("USD", "US gal", "Diesel Reefer", "1")]
  [InlineData("USD", "US gal", "Diesel", "0")]
  [InlineData("USD", "US gal", "Diesel", "-1")]
  public void UnsupportedQuotesCannotDisplaceEligibleDiesel(
    string currency,
    string unit,
    string product,
    string value
  )
  {
    var valid = Quote("Diesel", 3m);
    var invalid = Quote(
      product,
      decimal.Parse(value, CultureInfo.InvariantCulture),
      currency,
      unit
    );
    Assert.Same(
      valid,
      FuelDisplayPrices.Select(Station([invalid, valid]), Date).CashDiscount
    );
    Assert.Null(
      FuelDisplayPrices.Select(Station([invalid]), Date).CashDiscount
    );
  }

  [Fact]
  public void EqualPricesPreferTheNewestActiveQuoteIndependentlyOfInputOrder()
  {
    var current = Quote("Diesel", 3m);
    var old = current with { EffectiveFrom = Date.AddDays(-1) };
    Assert.Same(
      current,
      FuelDisplayPrices.Select(Station([old, current]), Date).CashDiscount
    );
    Assert.Same(
      current,
      FuelDisplayPrices.Select(Station([current, old]), Date).IftaDiscount
    );
  }

  private static FuelDiscountDto Quote(
    string product,
    decimal price,
    string currency = "USD",
    string unit = "US gal"
  ) =>
    new(
      currency,
      product,
      price + .5m,
      price,
      .5m,
      Date,
      Date,
      price - .25m,
      unit
    );

  private static FuelStationDto Station(List<FuelDiscountDto> discounts) =>
    new(
      Guid.NewGuid(),
      "test",
      "Station",
      "1 Road",
      "City",
      "NY",
      "",
      "US",
      40m,
      -79m,
      discounts
    );
}
