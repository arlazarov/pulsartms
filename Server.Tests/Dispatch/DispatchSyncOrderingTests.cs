using Application.Features.Dispatch.Models;
using Application.Models;
using Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

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
      1,
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
    return await new DispatchReadTicketStore(other).TakeAsync(
      DispatchImportTestData.Key,
      default
    );
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

  private static async Task<decimal?> PriceAsync(DispatchSyncFixture f)
  {
    await using var read = f.NewContext();
    return await read.Dispatches.Select(x => x.Price).SingleAsync();
  }
}
