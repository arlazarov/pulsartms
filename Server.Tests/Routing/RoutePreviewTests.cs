using Application.Features.Dispatch.Models;
using Application.Features.Dispatch.Queries;
using Application.Features.Fleet.Queries.GetFleetLocations;
using Application.Features.Fleet.Services;
using Application.Features.Routing.Services.Routes;
using Application.Models;
using Domain.Entities.Dispatch;
using Domain.Entities.Fleet;
using Domain.Models.Routing;
using Domain.Rules.Routing;
using Infrastructure.Persistence;
using MediatR;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;

namespace Server.Tests.Routing;

using Dispatch = global::Domain.Entities.Dispatch.Dispatch;

[Trait("Category", "Routing")]
[Trait("Kind", "Integration")]
public sealed class RoutePreviewTests
{
  [Fact]
  public async Task PreviewIncludesRoutesBeyondTheFirstHundredTruckRows()
  {
    await using var connection = new SqliteConnection("Data Source=:memory:");
    await connection.OpenAsync();
    await using var db = new AppDbContext(
      new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connection).Options
    );
    await db.Database.EnsureCreatedAsync();
    var truck = new Truck { Id = Guid.NewGuid() };
    var load = new Dispatch
    {
      Id = Guid.NewGuid(),
      Truck = truck,
      Status = "assigned",
      Stops =
      [
        new()
        {
          Id = Guid.NewGuid(),
          Sequence = 1,
          Job = "Pick Up",
        },
        new()
        {
          Id = Guid.NewGuid(),
          Sequence = 2,
          Job = "Drop Off",
        },
      ],
    };
    db.Dispatches.Add(load);
    var plan = new RoutePlan
    {
      Id = Guid.NewGuid(),
      DispatchId = load.Id,
      TruckId = truck.Id,
      Version = 1,
      Route = new()
      {
        Miles = 10,
        Legs = [new(10, 600, [new(40, -80), new(41, -80)])],
      },
    };
    db.DispatchRoutePlans.Add(
      new()
      {
        Id = plan.Id,
        DispatchId = load.Id,
        TruckId = truck.Id,
        InputHash = RoutePlanInputs.Hash(load, plan.Profile),
        PlanJson = RoutePlanStorage.Serialize(plan),
      }
    );
    await db.SaveChangesAsync();
    using var reads = TestCache.Create();
    using var displays = new RouteDisplayCache(reads);
    using var memory = new MemoryCache(new MemoryCacheOptions());
    using var services = new PlanningTestServices(db, reads: reads);
    var sender = new Sender(
      new()
      {
        TruckId = truck.Id,
        Dispatches = [new() { Id = load.Id, TruckId = truck.Id }],
      }
    );
    using var telemetry = new FleetTelemetryCache(memory, new TestCompany());
    var previews = new RoutePreviewService(
      db,
      services.PlanningInputs,
      sender,
      reads,
      displays,
      services.Routes,
      memory,
      new ServerTelemetry(new TestCompany()),
      telemetry
    );
    var result = await previews.GetAsync(default);
    Assert.Equal(truck.Id, Assert.Single(result).TruckId);
    Assert.Equal(new[] { 1, 2 }, sender.Pages);
    result[0].State!.Plan!.Route.Legs.Clear();
    var cached = await previews.GetAsync(default);
    Assert.NotEmpty(Assert.Single(cached).State!.Plan!.Route.Legs);
    Assert.Equal(new[] { 1, 2 }, sender.Pages);
    reads.Invalidate("board");
    await previews.GetAsync(default);
    Assert.Equal(new[] { 1, 2, 1, 2 }, sender.Pages);
    var profiles = new TruckPlanningProfileService(
      db,
      reads,
      services.Settings,
      services.ExchangeRates
    );
    plan.Tracking.AllStopsPassed = true;
    await new RoutePlanStore(
      db,
      reads,
      profiles,
      new SavedRoutePlanReader(db, NullLogger<SavedRoutePlanReader>.Instance)
    ).SaveAsync(await db.DispatchRoutePlans.SingleAsync(), plan, default);
    Assert.Empty(await previews.GetAsync(default));
    Assert.Equal(new[] { 1, 2, 1, 2, 1, 2 }, sender.Pages);
  }

  [Fact]
  public async Task ColdFleetPreviewCannotSelfDeadlockOnASharedReadCacheStripeCollision()
  {
    await using var connection = new SqliteConnection("Data Source=:memory:");
    await connection.OpenAsync();
    await using var db = new AppDbContext(
      new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connection).Options
    );
    await db.Database.EnsureCreatedAsync();
    using var reads = TestCache.Create();
    using var displays = new RouteDisplayCache(reads);
    using var memory = new MemoryCache(new MemoryCacheOptions());
    using var services = new PlanningTestServices(db, reads: reads);
    static uint Stripe(string key) =>
      (uint)StringComparer.Ordinal.GetHashCode(key) % 64;
    var formerOuterStripe = Stripe("read:route-previews:0:fleet");
    var innerKey = Enumerable
      .Range(0, 65536)
      .Select(i => $"preview-collision:{i}")
      .First(key => Stripe($"read:board:0:{key}") == formerOuterStripe);
    var entered = new TaskCompletionSource(
      TaskCreationOptions.RunContinuationsAsynchronously
    );
    var release = new TaskCompletionSource(
      TaskCreationOptions.RunContinuationsAsynchronously
    );
    var calls = 0;
    var sender = new Sender(new())
    {
      BeforeRequest = async () =>
      {
        await reads.GetAsync("board", innerKey, () => Task.FromResult(true));
        if (Interlocked.Increment(ref calls) == 1)
        {
          entered.SetResult();
          await release.Task;
        }
      },
    };
    using var telemetry = new FleetTelemetryCache(memory, new TestCompany());
    var service = new RoutePreviewService(
      db,
      services.PlanningInputs,
      sender,
      reads,
      displays,
      services.Routes,
      memory,
      new ServerTelemetry(new TestCompany()),
      telemetry
    );
    var first = service.GetAsync(default);
    await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
    var second = service.GetAsync(default);
    release.SetResult();
    var results = await Task.WhenAll(first, second)
      .WaitAsync(TimeSpan.FromSeconds(5));
    Assert.All(results, Assert.Empty);
    Assert.Equal(new[] { 1, 2 }, sender.Pages);
  }

  private sealed class Sender(TruckDispatchBoardResponse last) : ISender
  {
    public List<int> Pages { get; } = [];
    public Func<Task>? BeforeRequest { get; init; }

    public async Task<TResponse> Send<TResponse>(
      IRequest<TResponse> request,
      CancellationToken ct = default
    )
    {
      if (BeforeRequest is not null)
        await BeforeRequest();
      var board = Assert.IsType<GetDispatchBoardQuery>(request);
      Pages.Add(board.Page);
      var rows =
        board.Page == 1
          ? Enumerable
            .Range(0, 100)
            .Select(_ => new TruckDispatchBoardResponse())
            .ToList()
          : [last];
      return (TResponse)
        (object)
          RequestResponse<PaginatedList<TruckDispatchBoardResponse>>.Ok(
            new()
            {
              Items = rows,
              Page = board.Page,
              PageSize = 100,
              TotalCount = 101,
            }
          );
    }

    public Task Send<TRequest>(TRequest request, CancellationToken ct = default)
      where TRequest : IRequest => throw new NotSupportedException();

    public Task<object?> Send(object request, CancellationToken ct = default) =>
      throw new NotSupportedException();

    public IAsyncEnumerable<TResponse> CreateStream<TResponse>(
      IStreamRequest<TResponse> request,
      CancellationToken ct = default
    ) => throw new NotSupportedException();

    public IAsyncEnumerable<object?> CreateStream(
      object request,
      CancellationToken ct = default
    ) => throw new NotSupportedException();
  }
}
