using Application.Features.Fuel.Commands.ImportFuelDiscounts;
using Application.Features.Fuel.Interfaces;
using Application.Features.Fuel.Models;
using Application.Features.Fuel.Services;
using Infrastructure.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Server.Tests.Support;

namespace Server.Tests.Fuel;

[Trait("Category", "Fuel")]
[Trait("Kind", "Integration")]
public class FuelImportTests
{
  [Fact]
  public async Task NewStationAndBothAttachmentsAreSavedOnce()
  {
    await using var connection = new SqliteConnection("Data Source=:memory:");
    await connection.OpenAsync();
    await using var db = new AppDbContext(
      new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connection).Options
    );
    await db.Database.EnsureCreatedAsync();
    using var reads = TestCache.Create();
    var handler = new ImportFuelDiscountsHandler(
      db,
      new Provider([Import("CAD"), Import("USD")]),
      new FuelStationLookupService(
        new MemoryFuelStationLookupStore(),
        new Places(),
        TimeProvider.System
      ),
      reads
    );
    Assert.True((await handler.Handle(new(), default)).Success);
    Assert.Equal(1, await db.FuelStations.CountAsync());
    Assert.Equal(2, await db.FuelDiscounts.CountAsync());
    Assert.Equal(1, await db.FuelImportSources.CountAsync());
    Assert.All(
      await db.FuelDiscounts.ToListAsync(),
      x =>
      {
        Assert.Equal(.2m, x.Savings);
        Assert.Equal("Diesel", x.Product);
      }
    );
    Assert.Equal(0, (await handler.Handle(new(), default)).Response);
    Assert.Equal(2, await db.FuelDiscounts.CountAsync());
  }

  [Fact]
  public async Task FailureInSecondAttachmentRollsBackTheWholeMessage()
  {
    await using var connection = new SqliteConnection("Data Source=:memory:");
    await connection.OpenAsync();
    var options = new DbContextOptionsBuilder<AppDbContext>()
      .UseSqlite(connection)
      .Options;
    await using (var db = new AppDbContext(options))
    {
      await db.Database.EnsureCreatedAsync();
      using var reads = TestCache.Create();
      var second = Import("USD");
      second.Rows[0].StationId = "second";
      second.Rows[0].Name = "Fail";
      var handler = new ImportFuelDiscountsHandler(
        db,
        new Provider([Import("CAD"), second]),
        new FuelStationLookupService(
          new MemoryFuelStationLookupStore(),
          new Places(),
          TimeProvider.System
        ),
        reads
      );
      await Assert.ThrowsAsync<HttpRequestException>(
        () => handler.Handle(new(), default)
      );
    }
    await using var verify = new AppDbContext(options);
    Assert.Equal(0, await verify.FuelStations.CountAsync());
    Assert.Equal(0, await verify.FuelDiscounts.CountAsync());
    Assert.Equal(0, await verify.FuelImportSources.CountAsync());
  }

  private static FuelDiscountImportData Import(string currency) =>
    new()
    {
      MessageId = "message",
      AttachmentName = currency + ".csv",
      Currency = currency,
      EffectiveDate = new DateOnly(2026, 9, 5),
      Rows =
      [
        new()
        {
          StationId = "station",
          Name = "Station",
          State = "ON",
          City = "Toronto",
          RetailPrice = 2m,
          DiscountPrice = 1.8m,
        },
      ],
    };

  private sealed class Provider(IReadOnlyList<FuelDiscountImportData> imports)
    : IFuelDiscountProvider
  {
    public Task<IReadOnlyList<FuelDiscountImportData>> GetDiscountsAsync(
      IReadOnlyCollection<string> ids,
      CancellationToken ct = default
    ) => Task.FromResult(imports);
  }

  private sealed class Places : IPlaceSearchService
  {
    public Task<PlaceSearchResult?> SearchAsync(
      string query,
      CancellationToken ct = default
    ) =>
      query.StartsWith("Fail")
        ? throw new HttpRequestException("Test failure")
        : Task.FromResult<PlaceSearchResult?>(
          new()
          {
            Address = "Address",
            Latitude = 43,
            Longitude = -79,
          }
        );
  }
}
