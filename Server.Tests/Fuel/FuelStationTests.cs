using System.Globalization;
using Application.Features.Fuel.Queries.GetFuelStations;
using Domain.Entities.Fuel;
using Infrastructure.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Server.Tests.Fuel;

[Trait("Category", "Fuel")]
[Trait("Kind", "Integration")]
public class FuelStationTests
{
  [Fact]
  public async Task ResponseSelectsRoadDieselAfterIftaWithoutChangingTheFullDiscountList()
  {
    await using var connection = new SqliteConnection("Data Source=:memory:");
    await connection.OpenAsync();
    await using var db = new AppDbContext(
      new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connection).Options
    );
    await db.Database.EnsureCreatedAsync();
    using var reads = TestCache.Create();
    var date = new DateOnly(2026, 9, 9);
    var diesel = Discount("CAD", date);
    diesel.EffectiveFrom = date.AddDays(-1);
    var def = Discount("CAD", date);
    def.Product = "DEF";
    def.DiscountPrice = .1m;
    db.FuelStations.Add(
      new FuelStation
      {
        Id = Guid.NewGuid(),
        ExternalId = "display",
        Region = "ON",
        Latitude = 43,
        Longitude = -79,
        FuelDiscounts = [def, diesel],
      }
    );
    db.IftaTaxRates.Add(Rate(new(2026, 7, 1), "CAD", .2m));
    await db.SaveChangesAsync();
    var response = await new GetFuelStationsHandler(db, reads).Handle(
      new(date),
      default
    );
    var station = Assert.Single(response.Response!);
    Assert.Equal(2, station.Discounts.Count);
    Assert.Equal("Diesel", station.CashDiscount!.Product);
    Assert.Equal(2m, station.CashDiscount.DiscountPrice);
    Assert.Equal("L", station.IftaDiscount!.Unit);
    Assert.Equal(1.8m, station.IftaDiscount.PriceAfterIfta);
    Assert.Equal(2, await db.FuelDiscounts.CountAsync());
  }

  [Theory]
  [InlineData("OR", "Diesel", false, "2")]
  [InlineData(" or ", "", false, "2")]
  [InlineData("OR", "Diesel", true, "1.8")]
  [InlineData("WA", "Diesel", false, null)]
  [InlineData("OR", "DEF", false, null)]
  public async Task OregonMissingDieselRateUsesPriceWithoutDeduction(
    string region,
    string product,
    bool published,
    string? expected
  )
  {
    await using var connection = new SqliteConnection("Data Source=:memory:");
    await connection.OpenAsync();
    await using var db = new AppDbContext(
      new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connection).Options
    );
    await db.Database.EnsureCreatedAsync();
    using var reads = TestCache.Create();
    var date = new DateOnly(2026, 9, 5);
    var price = Discount("USD", date);
    price.Product = product;
    db.FuelStations.Add(
      new FuelStation
      {
        Id = Guid.NewGuid(),
        ExternalId = "oregon",
        Region = region,
        Latitude = 45,
        Longitude = -122,
        FuelDiscounts = [price],
      }
    );
    if (published)
    {
      var rate = Rate(new(2026, 7, 1), "USD", .2m);
      rate.Jurisdiction = "OR";
      db.IftaTaxRates.Add(rate);
    }
    await db.SaveChangesAsync();
    var response = await new GetFuelStationsHandler(db, reads).Handle(
      new(date),
      default
    );
    var actual = response.Response!.Single().Discounts.Single();
    Assert.Equal(
      expected is null
        ? (decimal?)null
        : decimal.Parse(expected, CultureInfo.InvariantCulture),
      actual.PriceAfterIfta
    );
    Assert.Equal(2m, actual.DiscountPrice);
  }

  [Theory]
  [InlineData("", "Diesel")]
  [InlineData("DEF", "DEF")]
  public async Task LegacyBvdDieselPricesRemainUsableWithoutReimport(
    string product,
    string expected
  )
  {
    await using var connection = new SqliteConnection("Data Source=:memory:");
    await connection.OpenAsync();
    await using var db = new AppDbContext(
      new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connection).Options
    );
    await db.Database.EnsureCreatedAsync();
    using var reads = TestCache.Create();
    var date = new DateOnly(2026, 9, 5);
    var discount = Discount("USD", date);
    discount.Product = product;
    db.FuelStations.Add(
      new FuelStation
      {
        Id = Guid.NewGuid(),
        ExternalId = "legacy",
        Region = "NY",
        Latitude = 40,
        Longitude = -79,
        FuelDiscounts = [discount],
      }
    );
    await db.SaveChangesAsync();
    var result = await new GetFuelStationsHandler(db, reads).Handle(
      new(date),
      default
    );
    Assert.Equal(
      expected,
      result.Response!.Single().Discounts.Single().Product
    );
    Assert.Equal(
      product,
      (await db.FuelDiscounts.AsNoTracking().SingleAsync()).Product
    );
  }

  [Theory]
  [InlineData(2026, 9, 5, false)]
  [InlineData(2026, 7, 1, false)]
  [InlineData(2026, 1, 1, false)]
  [InlineData(2026, 9, 5, true)]
  public async Task CurrentRateWinsWithPreviousQuarterFallbackPerJurisdiction(
    int year,
    int month,
    int day,
    bool published
  )
  {
    await using var connection = new SqliteConnection("Data Source=:memory:");
    await connection.OpenAsync();
    await using var db = new AppDbContext(
      new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connection).Options
    );
    await db.Database.EnsureCreatedAsync();
    using var reads = TestCache.Create();
    var date = new DateOnly(year, month, day);
    var quarter = new DateOnly(year, (month - 1) / 3 * 3 + 1, 1);
    var station = new FuelStation
    {
      Id = Guid.NewGuid(),
      ExternalId = "station",
      Region = "ON",
      Latitude = 43,
      Longitude = -79,
      FuelDiscounts = [Discount("CAD", date)],
    };
    db.FuelStations.Add(station);
    db.FuelStations.Add(
      new FuelStation
      {
        Id = Guid.NewGuid(),
        ExternalId = "us-station",
        Region = "NY",
        Latitude = 42,
        Longitude = -78,
        FuelDiscounts = [Discount("USD", date)],
      }
    );
    db.IftaTaxRates.AddRange(
      Rate(quarter.AddMonths(-3), "CAD", .2m),
      Rate(quarter.AddMonths(-3), "USD", .15m),
      Rate(quarter.AddMonths(3), "CAD", .9m),
      Rate(quarter.AddMonths(-6), "CAD", .8m)
    );
    if (published)
      db.IftaTaxRates.Add(Rate(quarter, "CAD", .3m));
    await db.SaveChangesAsync();
    var response = await new GetFuelStationsHandler(db, reads).Handle(
      new(date),
      default
    );
    var discounts = response.Response!.SelectMany(x => x.Discounts).ToList();
    Assert.Equal(
      published ? 1.7m : 1.8m,
      discounts.Single(x => x.Currency == "CAD").PriceAfterIfta
    );
    Assert.Equal(
      1.85m,
      discounts.Single(x => x.Currency == "USD").PriceAfterIfta
    );
    Assert.All(discounts, x => Assert.Equal(.2m, x.Savings));
  }

  [Fact]
  public async Task OlderFutureAndOtherJurisdictionRatesAreNotUsed()
  {
    await using var connection = new SqliteConnection("Data Source=:memory:");
    await connection.OpenAsync();
    await using var db = new AppDbContext(
      new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connection).Options
    );
    await db.Database.EnsureCreatedAsync();
    using var reads = TestCache.Create();
    var date = new DateOnly(2026, 9, 5);
    db.FuelStations.Add(
      new FuelStation
      {
        Id = Guid.NewGuid(),
        ExternalId = "station",
        Region = "ON",
        Latitude = 43,
        Longitude = -79,
        FuelDiscounts = [Discount("CAD", date)],
      }
    );
    var other = Rate(new(2026, 7, 1), "CAD", .4m);
    other.Jurisdiction = "QC";
    db.IftaTaxRates.AddRange(
      Rate(new(2026, 1, 1), "CAD", .2m),
      Rate(new(2026, 10, 1), "CAD", .3m),
      other
    );
    await db.SaveChangesAsync();
    var response = await new GetFuelStationsHandler(db, reads).Handle(
      new(date),
      default
    );
    Assert.Null(response.Response!.Single().Discounts.Single().PriceAfterIfta);
  }

  [Theory]
  [InlineData("CAD", "US gal", "0.3785411784", "1.9", "L")]
  [InlineData("USD", "L", "0.1", "1.6214588216", "US gal")]
  [InlineData("USD", "US gal", "0.1", "1.9", "US gal")]
  [InlineData("CAD", "unknown", "0.1", null, "L")]
  public async Task RatesAreConvertedToThePriceUnit(
    string currency,
    string rateUnit,
    string rateValue,
    string? expected,
    string expectedUnit
  )
  {
    await using var connection = new SqliteConnection("Data Source=:memory:");
    await connection.OpenAsync();
    await using var db = new AppDbContext(
      new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connection).Options
    );
    await db.Database.EnsureCreatedAsync();
    using var reads = TestCache.Create();
    var date = new DateOnly(2026, 9, 5);
    var rate = Rate(
      new(2026, 7, 1),
      currency,
      decimal.Parse(rateValue, CultureInfo.InvariantCulture)
    );
    rate.Unit = rateUnit;
    db.IftaTaxRates.Add(rate);
    db.FuelStations.Add(
      new FuelStation
      {
        Id = Guid.NewGuid(),
        ExternalId = "station",
        Region = rate.Jurisdiction,
        Latitude = 43,
        Longitude = -79,
        FuelDiscounts = [Discount(currency, date)],
      }
    );
    await db.SaveChangesAsync();
    var response = await new GetFuelStationsHandler(db, reads).Handle(
      new(date),
      default
    );
    var discount = response.Response!.Single().Discounts.Single();
    Assert.Equal(expectedUnit, discount.Unit);
    Assert.Equal(
      expected is null
        ? (decimal?)null
        : decimal.Parse(expected, CultureInfo.InvariantCulture),
      discount.PriceAfterIfta
    );
  }

  private static FuelDiscount Discount(string currency, DateOnly date) =>
    new()
    {
      Id = Guid.NewGuid(),
      Currency = currency,
      Product = "Diesel",
      DiscountPrice = 2m,
      RetailPrice = 2.2m,
      EffectiveFrom = date,
      EffectiveTo = date,
    };

  private static IftaTaxRate Rate(
    DateOnly from,
    string currency,
    decimal rate
  ) =>
    new()
    {
      Id = Guid.NewGuid(),
      Jurisdiction = currency == "CAD" ? "ON" : "NY",
      FuelType = "Diesel",
      Currency = currency,
      Unit = currency == "CAD" ? "L" : "US gal",
      Rate = rate,
      EffectiveFrom = from,
      EffectiveTo = from.AddMonths(3).AddDays(-1),
    };
}
