using System.Data.Common;
using Application.Features.Messaging.Commands;
using Application.Features.Messaging.Services;
using Application.Interfaces;
using Domain.Entities.Fleet;
using Domain.Entities.Messaging;
using Infrastructure.Persistence;
using Infrastructure.Synchronization;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Server.Tests.Support;

namespace Server.Tests.Messaging;

// Root's review of b5672e93: the synchronization lease is not HTTP
// liveness - a binary can answer webhooks holding no lease, or after
// losing it. A revision now sends nothing to the provider until an
// administrator records its release, after seeing the platform drain the
// revision before it; nothing inferred releases it.
[Trait("Category", "Messaging")]
[Trait("Kind", "Integration")]
public sealed class ReleaseSendHoldTests
{
  // The old binary still answers webhooks but holds no lease - the
  // synchronization row is free, expired, or held by a new owner. None of
  // it releases the hold; only this revision's own record does.
  [Fact]
  public async Task NoLeaseAndNoRecordStillHold()
  {
    await using var f = await Database.CreateAsync();
    var clock = new ManualTimeProvider(DateTimeOffset.UtcNow);
    var hold = TestSendHold.Required(f.Scopes, "rev-2", clock);
    var now = clock.GetUtcNow().UtcDateTime;

    Assert.True(await hold.HeldAsync(default));
    foreach (
      var (owner, until) in new[]
      {
        ("", now),
        ("expired", now.AddMinutes(-1)),
        ("new-binary", now.AddMinutes(3)),
      }
    )
    {
      await f.LeaseAsync(owner, until);
      clock.Advance(SendHold.Recheck);
      Assert.True(await hold.HeldAsync(default));
    }
    // The revision before was released; this one was not.
    await f.ReleaseAsync("rev-1");
    clock.Advance(SendHold.Recheck);
    Assert.True(await hold.HeldAsync(default));

    await f.ReleaseAsync("rev-2");
    Assert.True(await hold.HeldAsync(default));
    clock.Advance(SendHold.Recheck);
    Assert.False(await hold.HeldAsync(default));
  }

  // Where no release is required nothing is held and nothing is read.
  [Fact]
  public async Task WithoutARequiredReleaseNothingIsHeldOrRead() =>
    Assert.False(await TestSendHold.Open().HeldAsync(default));

  // Root: concurrent callers share one read, and a released revision is not
  // read again.
  [Fact]
  public async Task ConcurrentCallersShareOneRead()
  {
    await using var f = await Database.CreateAsync();
    var clock = new ManualTimeProvider(DateTimeOffset.UtcNow);
    var hold = TestSendHold.Required(f.Scopes, "rev-3", clock);
    f.Reads.Blocked = new();

    var callers = Enumerable
      .Range(0, 8)
      .Select(_ => hold.HeldAsync(default))
      .ToArray();
    await f.Reads.Started.Task.WaitAsync(TimeSpan.FromSeconds(10));
    f.Reads.Blocked.SetResult();
    Assert.All(await Task.WhenAll(callers), Assert.True);
    Assert.Equal(1, f.Reads.Count);

    await f.ReleaseAsync("rev-3");
    clock.Advance(SendHold.Recheck);
    Assert.False(await hold.HeldAsync(default));
    clock.Advance(SendHold.Recheck);
    Assert.False(await hold.HeldAsync(default));
    Assert.Equal(2, f.Reads.Count);
  }

  // The administrator's release: recorded once with who released it,
  // idempotent, and reported.
  [Fact]
  public async Task AnAdministratorReleasesTheRevisionOnce()
  {
    await using var f = await Database.CreateAsync();
    var clock = new ManualTimeProvider(DateTimeOffset.UtcNow);
    var hold = TestSendHold.Required(f.Scopes, "rev-4", clock);
    await using var scope = f.Scopes.CreateAsyncScope();
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    var handlers = new SendHoldHandlers(db, hold, new Caller(), clock);

    var before = (
      await handlers.Handle(new GetSendHoldQuery(), default)
    ).Response!;
    var first = (
      await handlers.Handle(new ReleaseSendsCommand(), default)
    ).Response!;
    clock.Advance(TimeSpan.FromMinutes(1));
    var second = (
      await handlers.Handle(new ReleaseSendsCommand(), default)
    ).Response!;

    Assert.True(before.Held);
    Assert.Null(before.ReleasedAt);
    Assert.Equal(
      ("rev-4", true, "admin-1"),
      (first.Revision, first.Required, first.ReleasedBy)
    );
    Assert.Equal(first.ReleasedAt, second.ReleasedAt);
    Assert.Single(await db.SendReleases.AsNoTracking().ToListAsync());
    clock.Advance(SendHold.Recheck);
    Assert.False(await hold.HeldAsync(default));
  }

  private sealed class Caller : ICurrentUser
  {
    public bool IsAuthenticated => true;
    public string IdentityUserId => "admin-1";
  }

  private sealed class Database : IAsyncDisposable
  {
    private readonly SqliteConnection connection = new("Data Source=:memory:");
    private ServiceProvider services = null!;
    public ReadCounter Reads { get; } = new();
    public IServiceScopeFactory Scopes =>
      services.GetRequiredService<IServiceScopeFactory>();

    public static async Task<Database> CreateAsync()
    {
      var f = new Database();
      await f.connection.OpenAsync();
      f.services = new ServiceCollection()
        .AddDbContext<AppDbContext>(options =>
          options.UseSqlite(f.connection).AddInterceptors(f.Reads)
        )
        .AddScoped<IAppDbContext>(p => p.GetRequiredService<AppDbContext>())
        .BuildServiceProvider();
      await using var scope = f.Scopes.CreateAsyncScope();
      await scope
        .ServiceProvider.GetRequiredService<AppDbContext>()
        .Database.EnsureCreatedAsync();
      return f;
    }

    public async Task LeaseAsync(string owner, DateTime until)
    {
      await using var scope = Scopes.CreateAsyncScope();
      var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
      var row = await db.SynchronizationCheckpoints.FindAsync(
        SynchronizationStore.Id
      );
      if (row is null)
        db.SynchronizationCheckpoints.Add(
          row = new SynchronizationCheckpoint { Id = SynchronizationStore.Id }
        );
      row.Owner = owner;
      row.LeaseUntil = until;
      row.UpdatedAt = until;
      await db.SaveChangesAsync();
    }

    public async Task ReleaseAsync(string revision)
    {
      await using var scope = Scopes.CreateAsyncScope();
      var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
      db.SendReleases.Add(
        new SendRelease
        {
          Id = Guid.NewGuid(),
          Revision = revision,
          ReleasedAt = DateTime.UtcNow,
          ReleasedBy = "admin",
        }
      );
      await db.SaveChangesAsync();
    }

    public async ValueTask DisposeAsync()
    {
      await services.DisposeAsync();
      await connection.DisposeAsync();
    }
  }

  // Counts the hold's reads of the release records; can hold the first
  // one open, so callers arriving meanwhile are seen to share it.
  private sealed class ReadCounter : DbCommandInterceptor
  {
    public int Count;
    public TaskCompletionSource? Blocked;
    public TaskCompletionSource Started { get; } =
      new(TaskCreationOptions.RunContinuationsAsynchronously);

    public override async ValueTask<
      InterceptionResult<DbDataReader>
    > ReaderExecutingAsync(
      DbCommand command,
      CommandEventData eventData,
      InterceptionResult<DbDataReader> result,
      CancellationToken cancellationToken = default
    )
    {
      if (
        command.CommandText.Contains("\"SendReleases\"")
        && !command.CommandText.Contains("INSERT")
      )
      {
        Interlocked.Increment(ref Count);
        Started.TrySetResult();
        if (Blocked is { } blocked)
          await blocked.Task;
      }
      return result;
    }
  }
}
