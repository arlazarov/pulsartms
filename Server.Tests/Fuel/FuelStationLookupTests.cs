using Application.Features.Fuel.Commands.ImportFuelDiscounts;
using Application.Features.Fuel.Interfaces;
using Application.Features.Fuel.Models;
using Application.Features.Fuel.Services;
using Domain.Entities.Fuel;
using Infrastructure.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Server.Tests.Support;

namespace Server.Tests.Fuel;

[Trait("Category", "Fuel")]
[Trait("Kind", "Integration")]
public sealed class FuelStationLookupTests
{
  [Fact]
  public async Task MissingCoordinatesRetryAfterCooldownAndCorrectedInputRetriesImmediately()
  {
    await using var connection = new SqliteConnection("Data Source=:memory:");
    await connection.OpenAsync();
    await using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connection).Options);
    await db.Database.EnsureCreatedAsync();
    db.FuelStations.Add(new FuelStation { Id = Guid.NewGuid(), ExternalId = "station" });
    await db.SaveChangesAsync();
    var retries = new MemoryFuelStationLookupStore();
    var clock = new Clock();
    var places = new Places();
    var lookups = new FuelStationLookupService(retries, places, clock);
    var row = new FuelDiscountImportRow { StationId = "station", Name = "Station", City = "Town", State = "TX" };
    await Sync(db, lookups, row);
    await db.SaveChangesAsync();
    await Sync(db, new(retries, places, clock), row);
    Assert.Equal(1, places.Calls);
    Assert.Null((await db.FuelStations.SingleAsync()).Latitude);
    clock.Now = clock.Now.AddHours(1);
    await Sync(db, lookups, row);
    Assert.Equal(2, places.Calls);
    row.City = "Correct Town";
    places.Result = new() { Address = "100 Main St", Latitude = 31, Longitude = -99 };
    await Sync(db, lookups, row);
    await db.SaveChangesAsync();
    db.ChangeTracker.Clear();
    var station = await db.FuelStations.SingleAsync();
    Assert.Equal(3, places.Calls);
    Assert.Equal("Correct Town", station.City);
    Assert.Equal(31, station.Latitude);
    await Sync(db, lookups, row);
    Assert.Equal(3, places.Calls);
    row.City = "New Town";
    places.Result = new() { Address = "200 Correct St", Latitude = 32, Longitude = -98 };
    await Sync(db, lookups, row);
    await db.SaveChangesAsync();
    Assert.Equal(4, places.Calls);
    Assert.Equal(32, (await db.FuelStations.SingleAsync()).Latitude);
  }

  private static async Task Sync(AppDbContext db, FuelStationLookupService lookups, FuelDiscountImportRow row) =>
    await FuelStationSync.SyncAsync(db, lookups, await FuelStationSync.PrepareAsync(db, lookups, [row]), [row]);

  private sealed class Clock : TimeProvider
  {
    public DateTimeOffset Now = DateTimeOffset.UtcNow;
    public override DateTimeOffset GetUtcNow() => Now;
  }

  private sealed class Places : IPlaceSearchService
  {
    public int Calls;
    public PlaceSearchResult? Result;
    public Task<PlaceSearchResult?> SearchAsync(string query, CancellationToken ct = default)
    {
      Calls++;
      return Task.FromResult(Result);
    }
  }
}
