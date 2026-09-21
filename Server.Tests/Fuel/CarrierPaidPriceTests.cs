using Application.Features.Fuel.Queries.GetFuelStations;
using Domain.Entities.Fuel;
using Domain.Models.Fuel;
using Domain.Rules;
using Infrastructure.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Server.Tests.Fuel;

// A carrier may have a discount program for a station, another program, or
// none at all. The price the carrier pays must be defined in all three cases,
// and a station with no known price must stay out of cost comparison while
// remaining a station.
[Trait("Category", "Fuel")]
[Trait("Kind", "Integration")]
public sealed class CarrierPaidPriceTests
{
  private static readonly DateOnly Day = new(2026, 9, 9);

  [Theory]
  [InlineData(2.2, 2.0, 2.0, 0.2)]
  [InlineData(2.2, 0.0, 2.2, 0.0)]
  public async Task ThePaidPriceIsTheProgramPriceOrTheKnownRetailPrice(
    double retail,
    double discount,
    double expectedPaid,
    double expectedSavings
  )
  {
    var station = Assert.Single(
      await ReadAsync((decimal)retail, (decimal)discount)
    );

    var price = Assert.Single(station.Discounts);
    Assert.Equal((decimal)expectedPaid, price.DiscountPrice);
    Assert.Equal((decimal)expectedSavings, price.Savings);
    Assert.NotNull(station.CashDiscount);
    Assert.Equal((decimal)expectedPaid, station.CashDiscount!.DiscountPrice);
  }

  [Fact]
  public async Task AStationWithNoKnownPriceIsNotOfferedAsACostComparison()
  {
    var station = Assert.Single(await ReadAsync(0m, 0m));

    Assert.Equal(0m, Assert.Single(station.Discounts).DiscountPrice);
    Assert.Null(Assert.Single(station.Discounts).PriceAfterIfta);
    Assert.Null(station.CashDiscount);
    Assert.Null(station.IftaDiscount);
    Assert.Empty(FuelDisplayPrices.EligibleQuotes(station.Discounts, Day));
  }

  [Fact]
  public async Task AProgramPriceStillCarriesItsIftaDeduction()
  {
    var station = Assert.Single(await ReadAsync(2.2m, 2.0m, ifta: true));

    Assert.NotNull(station.IftaDiscount);
    Assert.True(station.IftaDiscount!.PriceAfterIfta < 2.0m);
  }

  [Fact]
  public async Task ARetailOnlyPriceCarriesTheSameIftaDeduction()
  {
    var station = Assert.Single(await ReadAsync(2.2m, 0m, ifta: true));

    Assert.NotNull(station.IftaDiscount);
    Assert.True(station.IftaDiscount!.PriceAfterIfta < 2.2m);
  }

  private static async Task<List<FuelStationDto>> ReadAsync(
    decimal retail,
    decimal discount,
    bool ifta = false
  )
  {
    await using var connection = new SqliteConnection("Data Source=:memory:");
    await connection.OpenAsync();
    await using var db = new AppDbContext(
      new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connection).Options
    );
    await db.Database.EnsureCreatedAsync();
    using var reads = TestCache.Create();
    db.FuelStations.Add(
      new FuelStation
      {
        Id = Guid.NewGuid(),
        ExternalId = "paid-price",
        Region = "NY",
        Latitude = 43,
        Longitude = -79,
        FuelDiscounts =
        [
          new()
          {
            Id = Guid.NewGuid(),
            Currency = "USD",
            Product = "Diesel",
            RetailPrice = retail,
            DiscountPrice = discount,
            EffectiveFrom = Day,
            EffectiveTo = Day,
          },
        ],
      }
    );
    if (ifta)
      db.IftaTaxRates.Add(
        new IftaTaxRate
        {
          Id = Guid.NewGuid(),
          Jurisdiction = "NY",
          FuelType = "Diesel",
          Currency = "USD",
          Unit = "US gal",
          Rate = .25m,
          EffectiveFrom = Day.AddMonths(-2),
          EffectiveTo = Day.AddMonths(2),
        }
      );
    await db.SaveChangesAsync();

    var response = await new GetFuelStationsHandler(
      db,
      reads,
      TimeProvider.System
    ).Handle(new(Day), default);

    Assert.True(response.Success);
    return response.Response!;
  }
}
