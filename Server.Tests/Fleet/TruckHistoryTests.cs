using Application.Features.Fleet.Interfaces;
using Application.Features.Fleet.Models;
using Application.Features.Fleet.Queries.GetFleetLocations;
using Domain.Entities.Fleet;
using Infrastructure.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace Server.Tests.Fleet;

[Trait("Category", "Fleet")]
public class TruckHistoryTests
{
  [Fact]
  public async Task ReopeningHistoryReusesSavedPoints()
  {
    await using var connection = new SqliteConnection("Data Source=:memory:");
    await connection.OpenAsync();
    await using var db = new AppDbContext(
      new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connection).Options
    );
    await db.Database.EnsureCreatedAsync();
    var truck = new Truck { Id = Guid.NewGuid(), ExternalId = "history-test" };
    db.Trucks.Add(truck);
    await db.SaveChangesAsync();
    using var cache = new MemoryCache(new MemoryCacheOptions());
    var provider = new Provider();
    var handler = new GetTruckHistoryHandler(db, provider, cache, new());
    var query = new GetTruckHistoryQuery(
      truck.Id,
      DateTimeOffset.UtcNow.AddHours(-1),
      DateTimeOffset.UtcNow.AddHours(1)
    );
    await handler.Handle(query, default);
    Assert.Equal(0, provider.Calls);
    await handler.Handle(query with { Refresh = true }, default);
    await handler.Handle(query, default);
    Assert.Equal(1, provider.Calls);
  }

  [Fact]
  public void SimplificationKeepsTurnAndGapButRemovesStraightPoints()
  {
    var time = DateTime.UtcNow;
    var points = Enumerable
      .Range(0, 100)
      .Select(i => new VehicleLocationPoint
      {
        Latitude = 40,
        Longitude = -80m + i * .0001m,
        UpdatedAt = time.AddSeconds(i),
      })
      .ToList();
    points.Add(
      new()
      {
        Latitude = 40.01m,
        Longitude = -79.9901m,
        UpdatedAt = time.AddSeconds(110),
      }
    );
    points.Add(
      new()
      {
        Latitude = 41,
        Longitude = -79,
        UpdatedAt = time.AddMinutes(20),
      }
    );
    var result = TruckHistoryGeometry.Simplify(points);
    Assert.True(result.Count < 10);
    Assert.Contains(points[0], result);
    Assert.Contains(points[99], result);
    Assert.Contains(points[100], result);
    Assert.Contains(points[101], result);
  }

  private sealed class Provider : IFleetTelemetryProvider
  {
    public int Calls;

    public Task<IReadOnlyList<VehicleTelemetry>> GetVehicleTelemetryAsync(
      CancellationToken cancellationToken = default
    ) => throw new NotImplementedException();

    public Task<VehicleLocationStream> GetLocationStreamAsync(
      IReadOnlyCollection<string> vehicleIds,
      DateTime startTime,
      DateTime endTime,
      string? cursor = null,
      CancellationToken cancellationToken = default
    )
    {
      Calls++;
      return Task.FromResult(new VehicleLocationStream());
    }
  }
}
