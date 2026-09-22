using Application.Features.Fleet.Interfaces;
using Application.Features.Fleet.Queries.GetFleetLocations;
using Domain.Entities.Fleet;
using Domain.Models.Fleet;
using Infrastructure.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Server.Tests.Support;

namespace Server.Tests.Fleet;

[Trait("Category", "Fleet")]
[Trait("Kind", "Integration")]
public class TruckHistoryPublicationTests
{
  [Theory]
  [InlineData(false)]
  [InlineData(true)]
  public async Task PagesPublishOnlyAfterSuccessfulCompletion(bool fail)
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
    using var cache = new TruckHistoryCache(new TestCompany());
    var from = DateTimeOffset.UtcNow.AddHours(-1);
    var to = from.AddHours(2);
    var key = $"truck-history:{truck.Id}:{from.UtcDateTime:O}:{to:O}";
    var old = new TruckHistoryCache.Snapshot(
      [],
      from.UtcDateTime,
      DateTime.UtcNow.AddMinutes(-2)
    );
    cache.Set(key, old);
    var point = new VehicleLocationPoint
    {
      ExternalId = truck.ExternalId,
      UpdatedAt = from.UtcDateTime.AddMinutes(1),
    };
    var provider = new Provider(
      () => Assert.Same(old, cache.Get(key)),
      point,
      fail
    );
    var handler = new GetTruckHistoryHandler(db, provider, cache, new());
    var query = new GetTruckHistoryQuery(truck.Id, from, to, Refresh: true);
    if (fail)
    {
      await Assert.ThrowsAsync<InvalidOperationException>(
        () => handler.Handle(query, default)
      );
      Assert.Same(old, cache.Get(key));
    }
    else
    {
      await handler.Handle(query, default);
      Assert.Same(point, Assert.Single(cache.Get(key)!.Points));
    }
  }

  private sealed class Provider(
    Action check,
    VehicleLocationPoint point,
    bool fail
  ) : IFleetTelemetryProvider
  {
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
      check();
      if (cursor is not null && fail)
        throw new InvalidOperationException("Page failed.");
      return Task.FromResult(
        new VehicleLocationStream
        {
          Data = [point],
          HasNextPage = cursor is null,
          EndCursor = "next",
        }
      );
    }
  }
}
