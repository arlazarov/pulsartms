using Application.Features.Dispatch.Commands.SyncDispatche;
using Application.Features.Dispatch.Interfaces;
using Application.Features.Dispatch.Models;
using Domain.Entities.Fleet;
using Infrastructure.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace Server.Tests.Dispatch;

[Trait("Category", "Dispatch")]
[Trait("Kind", "Integration")]
public sealed class DispatchPreparationTests
{
  [Fact]
  public async Task ReplayedRawCoordinatesDoNotDirtyVerifiedRoutesButChangedSourceDirtiesTheirConnections()
  {
    await using var connection = new SqliteConnection("Data Source=:memory:");
    await connection.OpenAsync();
    await using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connection).Options);
    await db.Database.EnsureCreatedAsync();
    var truck = new Truck { Id = Guid.NewGuid(), ExternalId = "preparation", UnitNumber = "101", IsActive = true };
    db.Trucks.Add(truck);
    await db.SaveChangesAsync();
    var source = new ExternalDispatch { LoadNumber = 1, TruckNumber = "101", Status = "assigned",
      Stops = [new() { Sequence = 1, TruckNumber = "101", Address = "1 raw road", City = "Raw city", Latitude = 1, Longitude = 2 }] };
    var successor = new ExternalDispatch { LoadNumber = 2, TruckNumber = "101", Status = "assigned",
      Stops = [new() { Sequence = 1, TruckNumber = "101", Address = "2 raw road", Latitude = 3, Longitude = 4 }] };
    using var reads = TestCache.Create();
    using var memory = new MemoryCache(new MemoryCacheOptions());
    var preparation = TestCache.Preparation();
    var handler = new SyncDispatchesCommandHandler(db, new Provider([source, successor]), reads, memory, preparation);
    await handler.Handle(new(), default);
    var load = await db.Dispatches.Include(x => x.Stops).SingleAsync(x => x.LoadNumber == 1);
    var stop = Assert.Single(load.Stops);
    stop.Address = "1 Canonical Road";
    stop.City = "Canonical City";
    stop.Latitude = 40;
    stop.Longitude = -80;
    stop.AddressVerifiedAt = DateTime.UtcNow;
    await db.SaveChangesAsync();
    foreach (var work in preparation.Take(10)) preparation.Complete(work, "prepared", truck.Id);
    Assert.Equal(0, preparation.PendingCount);

    source.Stops[0].Notes = "Display-only change forces a fresh provider batch.";
    await handler.Handle(new(), default);
    Assert.Equal(0, preparation.PendingCount);
    Assert.Equal("1 Canonical Road", stop.Address);
    Assert.Equal(40m, stop.Latitude);
    Assert.Equal(-80m, stop.Longitude);
    Assert.NotNull(stop.AddressVerifiedAt);

    source.Stops[0].Address = "3 different road";
    await handler.Handle(new(), default);
    Assert.Null(stop.AddressVerifiedAt);
    Assert.Equal("3 different road", stop.Address);
    Assert.Equal(2, preparation.PendingCount);
    Assert.Equal(2, preparation.Take(10).Count);
  }

  private sealed class Provider(IReadOnlyList<ExternalDispatch> sources) : IDispatchProvider
  {
    public Task<IReadOnlyList<ExternalDispatch>> GetDispatchesAsync(CancellationToken ct = default) => Task.FromResult(sources);
    public Task<IReadOnlyList<ExternalDispatch>> GetDispatchesAsync(DateOnly from, DateOnly to, CancellationToken ct = default) => Task.FromResult(sources);
  }
}
