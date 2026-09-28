using Application.Caching;
using Application.Features.Dispatch.Models;
using Application.Features.Synchronization.Options;
using Application.Interfaces;
using Application.Models;
using Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Server.Tests.Support;

namespace Server.Tests.Dispatch;

// Audit F21. The provider is read before the pass's transaction, and the
// source carries no version. A pass in another process (the leased loop,
// a manual sync on another instance or during a revision change, the
// history tool) can commit its reading while this pass is still reading.
// Each pass takes a read ticket from the database before it reads; a load
// keeps the ticket of the pass that last wrote it, and an older reading
// never replaces a newer one. No clock is compared.
[Trait("Category", "Dispatch")]
[Trait("Kind", "Integration")]
public sealed class DispatchSyncOrderingTests
{
  // Another pass began reading after this one and committed during it:
  // this older reading is deferred, not written and not marked done, and
  // the next pass reads again and applies - also in a process that has
  // never seen the load before.
  [Theory]
  [InlineData(false)]
  [InlineData(true)]
  public async Task AnOlderReadingDoesNotReplaceANewerCommittedDuringIt(
    bool freshProcess
  )
  {
    await using var f = await DispatchSyncFixture.CreateAsync();
    var source = Load(100);
    f.Sources.Add(source);
    await PassAsync(f);
    if (freshProcess)
      f.Memory.Compact(1.0);
    source.Price = 150;
    f.DuringRead = async () =>
      await CommitAsync(f, await TicketAsync(f), 200, DateTime.UtcNow);

    Assert.True((await PassAsync(f)).Success);

    Assert.Equal(200, await PriceAsync(f));
    f.DuringRead = null;
    Assert.True((await PassAsync(f)).Success);
    Assert.Equal(150, await PriceAsync(f));
  }

  // Another pass began reading before this one and committed during it:
  // this reading is the newer one and applies - whatever time that pass
  // stamped, even one far ahead of this process's clock.
  [Theory]
  [InlineData(0)]
  [InlineData(60)]
  public async Task ANewerReadingReplacesAnOlderCommittedDuringIt(
    int skewMinutes
  )
  {
    await using var f = await DispatchSyncFixture.CreateAsync();
    var source = Load(100);
    f.Sources.Add(source);
    await PassAsync(f);
    var earlier = await TicketAsync(f);
    source.Price = 150;
    f.DuringRead = () =>
      CommitAsync(f, earlier, 200, DateTime.UtcNow.AddMinutes(skewMinutes));

    Assert.True((await PassAsync(f)).Success);

    Assert.Equal(150, await PriceAsync(f));
  }

  // A pass that finds the load as it read it changes nothing, yet stamps
  // its ticket: a reading that began before it, after the last write,
  // must not replace what it confirmed. (A load this process skips as
  // unchanged since its own last pass is not stamped; the half-hourly
  // repair pass stamps it.)
  [Fact]
  public async Task APassThatChangesNothingStillStampsItsTicket()
  {
    await using var f = await DispatchSyncFixture.CreateAsync();
    f.Sources.Add(Load(100));
    await PassAsync(f);
    f.Memory.Compact(1.0);

    Assert.Equal(0, (await PassAsync(f)).Response);

    await using var read = f.NewContext();
    Assert.Equal(
      2,
      await read.DispatchSourceLinks.Select(x => x.ReadTicket).SingleAsync()
    );
  }

  // Root's review of the tickets: two warm processes. B wrote V2 and has
  // since skipped it as unchanged; A, holding the older V1, took its
  // ticket after B's last write. While A reads, B polls again, finds V2
  // unchanged and returns without writing - its ticket stamps nothing -
  // and A then commits V1. B's next poll must put V2 back: through the
  // relay (B learns another process wrote) or, where no relay runs - the
  // history tool, a failed round - by itself.
  [Theory]
  [InlineData(true)]
  [InlineData(false)]
  public async Task AWriteBehindAWarmSkipIsRepairedByTheNextPoll(bool relay)
  {
    await using var f = await DispatchSyncFixture.CreateAsync();
    using var readsB = TestCache.Create();
    using var memoryB = new MemoryCache(new MemoryCacheOptions());
    var sourcesB = new List<ExternalDispatch> { Load(200) };
    async Task<RequestResponse<int>> PollB()
    {
      await using var db = f.NewContext();
      return await f.HandlerFor(db, sourcesB, readsB, memoryB)
        .Handle(new(), default);
    }
    await PollB();
    Assert.Equal(0, (await PollB()).Response);
    f.Sources.Add(Load(150));
    // B's skipped poll, as the database sees it: a ticket and nothing more.
    f.DuringRead = async () => await TicketAsync(f);

    Assert.True((await PassAsync(f)).Success);
    Assert.Equal(150, await PriceAsync(f));

    if (relay)
    {
      await using var services = new ServiceCollection()
        .AddScoped<IAppDbContext>(_ => f.NewContext())
        .BuildServiceProvider();
      foreach (var cache in new[] { f.Reads, readsB })
        await new CacheInvalidationRelay(
          cache,
          services.GetRequiredService<IServiceScopeFactory>(),
          Options.Create(new SynchronizationOptions()),
          TimeProvider.System,
          NullLogger<CacheInvalidationRelay>.Instance
        ).RunOnceAsync(default);
    }
    await PollB();

    Assert.Equal(200, await PriceAsync(f));
  }

  // A revision change: the previous binary takes no ticket and counts no
  // write, and commits an older reading behind the new one. Behind a warm
  // process of the new binary (a manual sync on its instance during the
  // overlap) the previous binary's relay round makes the next poll put the
  // newer reading back; a process that has not imported yet - the new
  // loop, which waits for the lease until the drain - reconciles every
  // load on its first pass.
  [Theory]
  [InlineData(true)]
  [InlineData(false)]
  public async Task APreviousBinarysTicketlessWriteIsRepaired(bool warm)
  {
    await using var f = await DispatchSyncFixture.CreateAsync();
    f.Sources.Add(Load(200));
    await PassAsync(f);
    if (warm)
      Assert.Equal(0, (await PassAsync(f)).Response);

    await TicketlessAsync(f, 150);
    if (warm)
    {
      using var previous = TestCache.Create();
      previous.Invalidate(ReadGroups.Dispatch);
      await using var services = new ServiceCollection()
        .AddScoped<IAppDbContext>(_ => f.NewContext())
        .BuildServiceProvider();
      foreach (var cache in new[] { previous, f.Reads })
        await new CacheInvalidationRelay(
          cache,
          services.GetRequiredService<IServiceScopeFactory>(),
          Options.Create(new SynchronizationOptions()),
          TimeProvider.System,
          NullLogger<CacheInvalidationRelay>.Instance
        ).RunOnceAsync(default);
    }
    else
      f.Memory.Compact(1.0);
    Assert.Equal(150, await PriceAsync(f));

    Assert.True((await PassAsync(f)).Success);

    Assert.Equal(200, await PriceAsync(f));
  }

  // Tickets only grow and are never shared, per carrier and provider.
  [Fact]
  public async Task TicketsGrowAndAreNeverShared()
  {
    await using var f = await DispatchSyncFixture.CreateAsync();
    var taken = new List<long>();
    for (var i = 0; i < 3; i++)
      taken.Add(await TicketAsync(f));

    Assert.Equal([1, 2, 3], taken);
    await using var other = f.NewContext();
    Assert.Equal(
      new(1, 0),
      await new DispatchReadTicketStore(other).TakeAsync("other", default)
    );
  }

  // A pass as the loop, a manual sync or the tool runs it: in its own
  // unit of work, never reading what an earlier pass left tracked.
  private static Task<RequestResponse<int>> PassAsync(DispatchSyncFixture f)
  {
    f.Db.ChangeTracker.Clear();
    return f.Handler.Handle(new(), default);
  }

  private static ExternalDispatch Load(decimal price) =>
    new()
    {
      LoadNumber = 7,
      Status = "assigned",
      Price = price,
      Stops = [new() { Sequence = 1, Job = "Pick Up" }],
    };

  private static async Task<long> TicketAsync(DispatchSyncFixture f)
  {
    await using var other = f.NewContext();
    return (
      await new DispatchReadTicketStore(other).TakeAsync(
        DispatchImportTestData.Key,
        default
      )
    ).Ticket;
  }

  // Another process's pass as the database sees it once committed: the
  // load written, stamped with that pass's clock, and its link carrying
  // that pass's ticket.
  private static async Task CommitAsync(
    DispatchSyncFixture f,
    long ticket,
    decimal price,
    DateTime syncedAt
  )
  {
    await using var other = f.NewContext();
    await using var transaction = await other.Database.BeginTransactionAsync();
    await other.Dispatches.ExecuteUpdateAsync(x =>
      x.SetProperty(d => d.Price, price)
        .SetProperty(d => d.LastSyncedAt, syncedAt)
    );
    await other.DispatchSourceLinks.ExecuteUpdateAsync(x =>
      x.SetProperty(l => l.ReadTicket, ticket)
    );
    await transaction.CommitAsync();
  }

  // The previous binary's pass as the database sees it: the load written
  // and stamped, its link's ticket and the write count untouched.
  private static async Task TicketlessAsync(
    DispatchSyncFixture f,
    decimal price
  )
  {
    await using var other = f.NewContext();
    await other.Dispatches.ExecuteUpdateAsync(x =>
      x.SetProperty(d => d.Price, price)
        .SetProperty(d => d.LastSyncedAt, DateTime.UtcNow)
    );
  }

  private static async Task<decimal?> PriceAsync(DispatchSyncFixture f)
  {
    await using var read = f.NewContext();
    return await read.Dispatches.Select(x => x.Price).SingleAsync();
  }
}
