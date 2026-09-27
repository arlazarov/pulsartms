using Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Server.Tests.Support;

namespace Server.Tests.Persistence;

// Audit F21: the import's read tickets are a raw upsert, and SQLite taking
// it says nothing about PostgreSQL. Passes in separate processes take them
// at once; no two may hold the same one.
[Trait("Category", "Database")]
[Trait("Kind", "Integration")]
public sealed class DispatchReadTicketPostgresTests
{
  [RequiresPostgresFact]
  public async Task TicketsTakenAtOnceAreDistinctAndOnlyGrow()
  {
    await using var postgres = await PostgresFixture.CreateAsync();
    var contexts = Enumerable
      .Range(0, 8)
      .Select(_ => postgres.Connect())
      .ToList();
    try
    {
      var taken = await Task.WhenAll(
        contexts.Select(db =>
          new DispatchReadTicketStore(db).TakeAsync("torqueai", default)
        )
      );

      Assert.Equal(Enumerable.Range(1, 8).Select(x => (long)x), taken.Order());
      await using var read = postgres.Connect();
      Assert.Equal(
        9,
        await new DispatchReadTicketStore(read).TakeAsync("torqueai", default)
      );
      Assert.Equal(
        1,
        await new DispatchReadTicketStore(read).TakeAsync("other", default)
      );
      Assert.Equal(
        9,
        await read
          .DispatchImportReads.Where(x => x.Provider == "torqueai")
          .Select(x => x.LastTicket)
          .SingleAsync()
      );
    }
    finally
    {
      foreach (var db in contexts)
        await db.DisposeAsync();
    }
  }
}
