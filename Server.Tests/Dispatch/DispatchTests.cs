using Application.Features.Dispatch.Commands.SyncDispatche;
using Application.Features.Dispatch.Interfaces;
using Application.Features.Dispatch.Models;
using Application.Features.Dispatch.Queries;
using Domain.Entities.Fleet;
using Infrastructure.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Server.Tests.Dispatch;

[Trait("Category", "Dispatch")]
public class DispatchTests
{
  [Fact]
  public async Task RepeatedSyncResolvesStopAssignmentsWithoutReplacingUnchangedStops()
  {
    await using var connection = new SqliteConnection("Data Source=:memory:");
    await connection.OpenAsync();
    await using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connection).Options);
    await db.Database.EnsureCreatedAsync();
    var source = new ExternalDispatch { LoadNumber = 123, CustomerName = "Customer", TruckNumber = "101", Status = "assigned",
      Stops = [new() { Sequence = 1, Job = "Pick Up", DriverName = "Test Driver", TruckNumber = "101" }] };
    using var reads = TestCache.Create();
    using var memory = new Microsoft.Extensions.Caching.Memory.MemoryCache(new Microsoft.Extensions.Caching.Memory.MemoryCacheOptions());
    var preparation = new Application.Features.Routing.Background.RoutePreparationQueue(
      Microsoft.Extensions.Options.Options.Create(new Application.Features.Routing.Options.RoutePreparationOptions()), TimeProvider.System);
    var handler = new SyncDispatchesCommandHandler(db, new Provider([source]), reads, memory, preparation);
    await handler.Handle(new(), default);
    var stop = await db.DispatchStops.SingleAsync();
    var stopId = stop.Id;
    Assert.Null(stop.TruckId);
    var truck = new Truck { Id = Guid.NewGuid(), ExternalId = "external", UnitNumber = "101", IsActive = true };
    var driver = new Driver { Id = Guid.NewGuid(), ExternalId = "driver", Name = "Test Driver", IsActive = true };
    db.Trucks.Add(truck); db.Drivers.Add(driver); await db.SaveChangesAsync();
    // Match fleet synchronization's invalidation after a catalog change.
    memory.Remove("dispatch-sync-signature");
    await handler.Handle(new(), default);
    Assert.Equal(stopId, stop.Id);
    Assert.Equal(truck.Id, stop.TruckId);
    Assert.Equal(driver.Id, stop.DriverId);
    using var planning = new PlanningTestServices(db);
    var list = await new GetDispatchQueryHandler(db, planning.Deadheads).Handle(new(1, 20, "Customer", "assigned", truck.Id), default);
    Assert.Equal(1, list.Response!.TotalCount);
    Assert.Empty(list.Response.Items.Single().Stops);
    Assert.Null(list.Response.Items.Single().Eta);
    var detail = await new GetDispatchByIdHandler(db, planning.Forecasts).Handle(new(list.Response.Items.Single().Id), default);
    Assert.Single(detail.Response!.Stops);
    Assert.Equal(truck.Id, detail.Response.TruckId);
    var missing = await new GetDispatchByIdHandler(db, planning.Forecasts).Handle(new(Guid.NewGuid()), default);
    Assert.Equal(404, missing.StatusCode);
  }

  [Theory]
  [InlineData(0, 20)]
  [InlineData(1, 0)]
  [InlineData(1, 101)]
  public void InvalidPaginationIsRejected(int page, int pageSize)
    => Assert.False(new GetDispatchValidator().Validate(new GetDispatchQuery(page, pageSize)).IsValid);

  private sealed class Provider(IReadOnlyList<ExternalDispatch> sources) : IDispatchProvider
  {
    public Task<IReadOnlyList<ExternalDispatch>> GetDispatchesAsync(CancellationToken ct = default) => Task.FromResult(sources);
    public Task<IReadOnlyList<ExternalDispatch>> GetDispatchesAsync(DateOnly from, DateOnly to, CancellationToken ct = default) => Task.FromResult(sources);
  }
}
