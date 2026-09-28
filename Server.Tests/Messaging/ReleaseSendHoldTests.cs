using Application.Features.Messaging.Services;
using Application.Interfaces;
using Domain.Entities.Fleet;
using Infrastructure.Persistence;
using Infrastructure.Synchronization;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Server.Tests.Support;

namespace Server.Tests.Messaging;

// Root's review of b541431e: a snapshot of nothing in flight cannot close
// the release's send window. This binary holds its own sends while a
// binary from before audit F27 runs, which it tells by the
// synchronization lease: held now, by an owner without the marker.
[Trait("Category", "Messaging")]
[Trait("Kind", "Integration")]
public sealed class ReleaseSendHoldTests
{
  [Theory]
  [InlineData("0123456789abcdef0123456789abcdef", 1, true)]
  [InlineData(IPreviousBinary.Marker + "0123", 1, false)]
  [InlineData("", 1, false)]
  [InlineData("0123456789abcdef0123456789abcdef", -1, false)]
  public async Task APreviousBinaryIsAnUnmarkedOwnerOfALiveLease(
    string owner,
    int minutes,
    bool runs
  )
  {
    await using var connection = new SqliteConnection("Data Source=:memory:");
    await connection.OpenAsync();
    await using var db = new AppDbContext(
      new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connection).Options
    );
    await db.Database.EnsureCreatedAsync();
    var now = DateTime.UtcNow;
    db.SynchronizationCheckpoints.AddRange(
      new SynchronizationCheckpoint
      {
        Id = SynchronizationStore.Id,
        Owner = owner,
        LeaseUntil = now.AddMinutes(minutes),
        UpdatedAt = now,
      },
      // Another loop's lease, held by an unmarked owner, is not the
      // synchronization loop's and says nothing about the binary.
      new SynchronizationCheckpoint
      {
        Id = Guid.NewGuid(),
        Owner = "0123456789abcdef0123456789abcdef",
        LeaseUntil = now.AddMinutes(1),
        UpdatedAt = now,
      }
    );
    await db.SaveChangesAsync();

    Assert.Equal(
      runs,
      await new PreviousBinary(db, TimeProvider.System).RunsAsync(default)
    );
  }

  // Held while the previous binary runs, asked again only every Recheck;
  // once it is gone the hold is released for good in this process.
  [Fact]
  public async Task TheHoldIsAskedAgainOnlyAfterRecheckAndReleasesForGood()
  {
    var clock = new ManualTimeProvider(DateTimeOffset.UtcNow);
    var (hold, previous) = TestSendHold.With(new(true), clock);

    Assert.True(await hold.HeldAsync(default));
    Assert.True(await hold.HeldAsync(default));
    Assert.Equal(1, previous.Asked);

    previous.Runs = false;
    Assert.True(await hold.HeldAsync(default));
    clock.Advance(SendHold.Recheck);
    Assert.False(await hold.HeldAsync(default));
    Assert.Equal(2, previous.Asked);

    previous.Runs = true;
    clock.Advance(SendHold.Recheck);
    Assert.False(await hold.HeldAsync(default));
    Assert.Equal(2, previous.Asked);
  }
}
