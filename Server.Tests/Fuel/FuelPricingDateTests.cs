using Application.Features.Fuel.Models;
using Application.Features.Fuel.Queries.GetFuelStations;
using Domain.Entities.Fuel;
using Domain.Rules;
using Infrastructure.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

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

  // An omitted request date selects the pricing day, not the UTC day the
  // server happens to run in.
  [Theory]
  [InlineData("2026-09-06T00:30:00Z", "2026-09-05")]
  [InlineData("2026-09-06T04:00:00Z", "2026-09-06")]
  public async Task AnOmittedRequestDateResolvesToTheBusinessDay(
    string utc,
    string expected
  )
  {
    var day = DateOnly.Parse(expected);
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
        ExternalId = "pricing-day",
        Region = "ON",
        Latitude = 43,
        Longitude = -79,
        FuelDiscounts =
        [
          new()
          {
            Id = Guid.NewGuid(),
            Currency = "CAD",
            Product = "Diesel",
            DiscountPrice = 2m,
            RetailPrice = 2.2m,
            EffectiveFrom = day,
            EffectiveTo = day,
          },
        ],
      }
    );
    await db.SaveChangesAsync();

    var response = await new GetFuelStationsHandler(
      db,
      reads,
      new FixedClock(DateTime.Parse(utc).ToUniversalTime())
    ).Handle(new(), default);

    Assert.True(response.Success);
    Assert.Equal(
      day,
      Assert.Single(Assert.Single(response.Response!).Discounts).EffectiveFrom
    );
  }

  private sealed class FixedClock(DateTime utc) : TimeProvider
  {
    public override DateTimeOffset GetUtcNow() => new(utc, TimeSpan.Zero);
  }
}
