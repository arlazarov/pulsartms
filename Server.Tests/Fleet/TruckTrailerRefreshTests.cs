using System.Data.Common;
using Application.Behaviors;
using Application.Caching;
using Application.Concurrency;
using Application.Features.Fleet.Services;
using Application.Features.Routing.Background;
using Application.Models;
using Domain.Entities.Dispatch;
using Domain.Entities.Fleet;
using Domain.Policies;
using Infrastructure.Persistence;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Load = Domain.Entities.Dispatch.Dispatch;

namespace Server.Tests.Fleet;

// A dispatcher's committed change to current work moves the trailer before
// the request answers: only the trucks it touched, both trucks of a
// transfer, and nothing when the request failed. The two-minute
// synchronization stays as recovery.
[Trait("Category", "Fleet")]
[Trait("Kind", "Integration")]
public sealed class TruckTrailerRefreshTests
{
  public sealed record Change : IRequest<RequestResponse<int>>;

  [Fact]
  public async Task ATransferMovesTheTrailerBetweenBothTrucksAtOnce()
  {
    await using var f = await Fixture.CreateAsync();
    var from = await f.TruckAsync("11005");
    var to = await f.TruckAsync("11006");
    var load = await f.LoadAsync(from, "55904");
    await f.SettleAsync();
    Assert.NotNull((await f.Row(from)).TrailerId);

    // What SetTruckAssignment does: commit, then name both trucks.
    await f.SendAsync(async () =>
    {
      await f
        .Db.Dispatches.Where(x => x.Id == load)
        .ExecuteUpdateAsync(x => x.SetProperty(d => d.PlanningTruckId, to));
      f.Queue.MarkTruckDirty(from, f.Reads);
      f.Queue.MarkTruckDirty(to, f.Reads);
      return RequestResponse<int>.Ok(1);
    });

    Assert.Null((await f.Row(from)).TrailerId);
    Assert.Equal("load", (await f.Row(to)).TrailerSource);
    Assert.NotNull((await f.Row(to)).TrailerId);
  }

  [Fact]
  public async Task AFailedOrThrowingRequestRefreshesNothing()
  {
    await using var f = await Fixture.CreateAsync();
    var truck = await f.TruckAsync("11005");
    await f.LoadAsync(truck, "55904");

    await f.SendAsync(() =>
    {
      f.Queue.MarkTruckDirty(truck, f.Reads);
      return Task.FromResult(RequestResponse<int>.Fail("refused", 409));
    });
    Assert.Null((await f.Row(truck)).TrailerId);
    Assert.Empty(await f.Db.Trailers.ToListAsync());

    await Assert.ThrowsAsync<InvalidOperationException>(
      () =>
        f.SendAsync(() =>
        {
          f.Queue.MarkTruckDirty(truck, f.Reads);
          throw new InvalidOperationException("rolled back");
        })
    );
    Assert.Null((await f.Row(truck)).TrailerId);
  }

  [Fact]
  public async Task OnlyTheTouchedTrucksAreReadAndChanged()
  {
    var counter = new CommandCounter();
    await using var f = await Fixture.CreateAsync(counter);
    var touched = await f.TruckAsync("11005");
    await f.LoadAsync(touched, "55904");
    // An untouched truck whose stored trailer is out of date: a fleet-wide
    // pass would correct it, a targeted one must leave it for recovery.
    var untouched = await f.TruckAsync("11006");
    await f.LoadAsync(untouched, "60001");

    await f.SendAsync(() =>
    {
      f.Queue.MarkTruckDirty(touched, f.Reads);
      return Task.FromResult(RequestResponse<int>.Ok(1));
    });
    Assert.NotNull((await f.Row(touched)).TrailerId);
    Assert.Null((await f.Row(untouched)).TrailerId);

    // The same work for one touched truck costs the same beside a larger
    // fleet.
    async Task<int> CostAsync()
    {
      f.Db.ChangeTracker.Clear();
      counter.Reads = 0;
      await TruckTrailerAssignments.ResolveTrucksAsync(
        f.Db,
        [touched],
        default
      );
      return counter.Reads;
    }
    var small = await CostAsync();
    for (var i = 0; i < 15; i++)
      await f.LoadAsync(
        await f.TruckAsync((20000 + i).ToString()),
        (70000 + i).ToString()
      );
    Assert.Equal(small, await CostAsync());
  }

  [Fact]
  public async Task ItWaitsForARunningSynchronizationThenRefreshes()
  {
    await using var f = await Fixture.CreateAsync();
    var truck = await f.TruckAsync("11005");
    await f.LoadAsync(truck, "55904");
    await ProcessGates.Fleet.WaitAsync();
    var request = f.SendAsync(() =>
    {
      f.Queue.MarkTruckDirty(truck, f.Reads);
      return Task.FromResult(RequestResponse<int>.Ok(1));
    });
    await Task.Delay(200);
    Assert.False(request.IsCompleted);
    Assert.Null((await f.Row(truck)).TrailerId);
    ProcessGates.Fleet.Release();
    await request;
    Assert.NotNull((await f.Row(truck)).TrailerId);
  }

  [Fact]
  public async Task TelemetryHeldByAnotherTruckIsNotTakenByAManualChange()
  {
    await using var f = await Fixture.CreateAsync();
    var reported = await f.TruckAsync("11006");
    var trailer = new Trailer
    {
      Id = Guid.NewGuid(),
      UnitNumber = "55904",
      IsActive = true,
    };
    f.Db.Trailers.Add(trailer);
    await f.Db.SaveChangesAsync();
    await f
      .Db.Trucks.Where(x => x.Id == reported)
      .ExecuteUpdateAsync(x =>
        x.SetProperty(t => t.TelemetryTrailerKnown, true)
          .SetProperty(t => t.TelemetryTrailerId, trailer.Id)
          .SetProperty(t => t.TrailerId, trailer.Id)
          .SetProperty(t => t.TrailerSource, "telemetry")
      );
    var manual = await f.TruckAsync("11005");
    await f.LoadAsync(manual, "55904");

    await f.SendAsync(() =>
    {
      f.Queue.MarkTruckDirty(manual, f.Reads);
      return Task.FromResult(RequestResponse<int>.Ok(1));
    });

    Assert.Equal(trailer.Id, (await f.Row(reported)).TrailerId);
    var row = await f.Row(manual);
    Assert.Equal((null, trailer.Id), (row.TrailerId, row.TrailerConflictId));
  }

  private sealed class Fixture : IAsyncDisposable
  {
    public required PlanningRefreshFixture Refresh { get; init; }
    public AppDbContext Db => Refresh.Db;
    public ReadCache Reads { get; } = TestCache.Create();
    public RoutePreparationQueue Queue { get; } =
      new(Options.Create(new RoutePreparationOptions()), TimeProvider.System);

    public static async Task<Fixture> CreateAsync(
      CommandCounter? counter = null
    ) =>
      new()
      {
        Refresh = await PlanningRefreshFixture.CreateAsync(services =>
        {
          if (counter is not null)
            services.ConfigureDbContext<AppDbContext>(o =>
              o.AddInterceptors(counter)
            );
        }),
      };

    public async Task SendAsync(Func<Task<RequestResponse<int>>> handler)
    {
      Db.ChangeTracker.Clear();
      await new TruckTrailerRefreshBehavior<Change, RequestResponse<int>>(
        Db,
        Reads,
        NullLogger<
          TruckTrailerRefreshBehavior<Change, RequestResponse<int>>
        >.Instance
      ).Handle(new(), _ => handler(), default);
      Db.ChangeTracker.Clear();
    }

    public async Task SettleAsync()
    {
      Db.ChangeTracker.Clear();
      await TruckTrailerAssignments.ResolveAsync(Db, default);
      await Db.SaveChangesAsync();
      Db.ChangeTracker.Clear();
    }

    public Task<Truck> Row(Guid id) =>
      Db.Trucks.AsNoTracking().SingleAsync(x => x.Id == id);

    public async Task<Guid> TruckAsync(string unit)
    {
      var truck = new Truck
      {
        Id = Guid.NewGuid(),
        ExternalId = "v-" + unit,
        UnitNumber = unit,
        IsActive = true,
      };
      Db.Trucks.Add(truck);
      await Db.SaveChangesAsync();
      return truck.Id;
    }

    public async Task<Guid> LoadAsync(Guid truck, string trailer)
    {
      var id = Guid.NewGuid();
      Db.Dispatches.Add(
        new Load
        {
          Id = id,
          LoadNumber = Random.Shared.Next(1000, 999999),
          Status = "in_transit",
          TruckId = truck,
          TrailerNumber = trailer,
          Stops =
          [
            new DispatchStop
            {
              Id = Guid.NewGuid(),
              DispatchId = id,
              Sequence = 1,
              Job = "Drop Off",
              TrailerNumber = trailer,
            },
          ],
        }
      );
      await Db.SaveChangesAsync();
      return id;
    }

    public ValueTask DisposeAsync()
    {
      Reads.Dispose();
      return Refresh.DisposeAsync();
    }
  }

  private sealed class CommandCounter : DbCommandInterceptor
  {
    public int Reads { get; set; }

    public override ValueTask<
      InterceptionResult<DbDataReader>
    > ReaderExecutingAsync(
      DbCommand command,
      CommandEventData eventData,
      InterceptionResult<DbDataReader> result,
      CancellationToken cancellationToken = default
    )
    {
      Reads++;
      return ValueTask.FromResult(result);
    }
  }
}
