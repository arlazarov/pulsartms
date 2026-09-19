using System.Data.Common;
using Domain.Entities.Execution;
using Infrastructure.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Server.Tests.Routing;

[Trait("Category", "Routing")]
[Trait("Kind", "Integration")]
public sealed class ExecutionPlanningStoreTests : IAsyncLifetime
{
  private readonly SqliteConnection connection = new("Data Source=:memory:");
  private readonly BeforeClaim probe = new();
  private static readonly DateTime Now = new(
    2026,
    9,
    17,
    12,
    0,
    0,
    DateTimeKind.Utc
  );

  public async Task InitializeAsync()
  {
    await connection.OpenAsync();
    await using var db = Context();
    await db.Database.EnsureCreatedAsync();
  }

  public async Task DisposeAsync() => await connection.DisposeAsync();

  [Fact]
  public async Task IndependentContextsClaimDifferentDueWork()
  {
    var first = await AddAsync();
    var second = await AddAsync(available: Now.AddSeconds(-1));
    await using var owner = Context();
    await using var follower = Context();
    var claimed = await new ExecutionPlanningStore(owner).ClaimAsync(
      Now,
      default
    );
    Assert.Equal(second.Id, claimed?.Id);
    Assert.Equal(1, claimed?.Attempts);
    Assert.NotNull(claimed?.LeaseId);
    Assert.Equal(Now.AddMinutes(10), claimed?.LeaseUntil);
    Assert.Equal(
      first.Id,
      (await new ExecutionPlanningStore(follower).ClaimAsync(Now, default))?.Id
    );
    Assert.Null(
      await new ExecutionPlanningStore(owner).ClaimAsync(Now, default)
    );
  }

  [Fact]
  public async Task RestartRecoversExpiredWorkAndRejectsPreviousOwner()
  {
    var request = await AddAsync();
    await using var first = Context();
    var old = Assert.IsType<ExecutionPlanningChange>(
      await new ExecutionPlanningStore(first).ClaimAsync(Now, default)
    );
    await using var restarted = Context();
    var store = new ExecutionPlanningStore(restarted);
    var later = Now.AddMinutes(10);
    var current = Assert.IsType<ExecutionPlanningChange>(
      await store.ClaimAsync(later, default)
    );
    Assert.Equal(request.Id, current.Id);
    Assert.Equal(2, current.Attempts);
    Assert.NotEqual(old.LeaseId, current.LeaseId);
    Assert.False(await store.CompleteAsync(old, later, true, default));
    Assert.True(await store.CompleteAsync(current, later, true, default));
    Assert.False(await store.CompleteAsync(current, later, false, default));
    Assert.Null(await store.ClaimAsync(later.AddDays(1), default));
  }

  [Theory]
  [InlineData(true)]
  [InlineData(false)]
  public async Task ExpiredOwnerCannotCompleteOrReschedule(bool succeeded)
  {
    await AddAsync();
    await using var db = Context();
    var store = new ExecutionPlanningStore(db);
    var work = Assert.IsType<ExecutionPlanningChange>(
      await store.ClaimAsync(Now, default)
    );
    Assert.False(
      await store.CompleteAsync(
        work,
        work.LeaseUntil!.Value,
        succeeded,
        default
      )
    );
    var saved = await db.ExecutionPlanningChanges.SingleAsync();
    Assert.Null(saved.CompletedAt);
    Assert.Equal(Now, saved.AvailableAt);
    Assert.Equal(work.LeaseId, saved.LeaseId);
  }

  [Fact]
  public async Task UnclaimedRequestCannotBeAcknowledged()
  {
    var work = await AddAsync();
    await using var db = Context();
    Assert.False(
      await new ExecutionPlanningStore(db).CompleteAsync(
        work,
        Now,
        true,
        default
      )
    );
    Assert.Null((await db.ExecutionPlanningChanges.SingleAsync()).CompletedAt);
  }

  [Fact]
  public async Task FailureRetainsDurableBackoffAcrossContexts()
  {
    await AddAsync();
    await using var owner = Context();
    var store = new ExecutionPlanningStore(owner);
    var work = Assert.IsType<ExecutionPlanningChange>(
      await store.ClaimAsync(Now, default)
    );
    Assert.True(await store.CompleteAsync(work, Now, false, default));
    await using var restarted = Context();
    var retry = new ExecutionPlanningStore(restarted);
    Assert.Null(await retry.ClaimAsync(Now.AddSeconds(29), default));
    var next = Assert.IsType<ExecutionPlanningChange>(
      await retry.ClaimAsync(Now.AddSeconds(30), default)
    );
    Assert.Equal(work.Id, next.Id);
    Assert.Equal(2, next.Attempts);
  }

  [Fact]
  public async Task FinishingOldRevisionDoesNotAcknowledgeNewRequest()
  {
    var old = await AddAsync();
    await using var db = Context();
    var store = new ExecutionPlanningStore(db);
    var running = Assert.IsType<ExecutionPlanningChange>(
      await store.ClaimAsync(Now, default)
    );
    var newer = await AddAsync(old, Now);
    Assert.True(await store.CompleteAsync(running, Now, true, default));
    var next = Assert.IsType<ExecutionPlanningChange>(
      await store.ClaimAsync(Now, default)
    );
    Assert.Equal(newer.Id, next.Id);
    Assert.Equal(old.ExecutionLegId, next.ExecutionLegId);
    Assert.Equal(old.AssignmentRevision + 1, next.AssignmentRevision);
  }

  [Fact]
  public async Task ClaimRechecksRetryDeadlineAfterCandidateRead()
  {
    var request = await AddAsync();
    probe.Action = async () =>
    {
      await using var other = Context();
      await other
        .ExecutionPlanningChanges.Where(x => x.Id == request.Id)
        .ExecuteUpdateAsync(setters =>
          setters.SetProperty(x => x.AvailableAt, Now.AddMinutes(1))
        );
    };
    await using var db = Context();
    Assert.Null(await new ExecutionPlanningStore(db).ClaimAsync(Now, default));
    var saved = await db.ExecutionPlanningChanges.SingleAsync();
    Assert.Equal(Now.AddMinutes(1), saved.AvailableAt);
    Assert.Null(saved.LeaseId);
    Assert.Equal(0, saved.Attempts);
  }

  [Fact]
  public async Task PruningRetainsPendingAndRecentlyCompletedWork()
  {
    var pending = await AddAsync();
    var recent = await AddAsync(available: Now.AddDays(-1));
    var old = await AddAsync(available: Now.AddDays(-8));
    await using var db = Context();
    foreach (var work in new[] { old, recent })
      await db
        .ExecutionPlanningChanges.Where(x => x.Id == work.Id)
        .ExecuteUpdateAsync(setters =>
          setters.SetProperty(x => x.CompletedAt, work.AvailableAt)
        );
    await new ExecutionPlanningStore(db).PruneAsync(Now.AddDays(-7), default);
    var ids = await db.ExecutionPlanningChanges.Select(x => x.Id).ToListAsync();
    Assert.Equal(2, ids.Count);
    Assert.Contains(pending.Id, ids);
    Assert.Contains(recent.Id, ids);
  }

  private async Task<ExecutionPlanningChange> AddAsync(
    ExecutionPlanningChange? previous = null,
    DateTime? available = null
  )
  {
    await using var db = Context();
    var work = new ExecutionPlanningChange
    {
      DispatchId = previous?.DispatchId ?? Guid.NewGuid(),
      TruckId = previous?.TruckId ?? Guid.NewGuid(),
      ExecutionLegId = previous?.ExecutionLegId ?? Guid.NewGuid(),
      AssignmentRevision = (previous?.AssignmentRevision ?? 0) + 1,
      RequestedAt = Now,
      AvailableAt = available ?? Now,
    };
    db.ExecutionPlanningChanges.Add(work);
    await db.SaveChangesAsync();
    return work;
  }

  private AppDbContext Context() =>
    new(
      new DbContextOptionsBuilder<AppDbContext>()
        .UseSqlite(connection)
        .AddInterceptors(probe)
        .Options
    );

  private sealed class BeforeClaim : DbCommandInterceptor
  {
    public Func<Task>? Action { get; set; }

    public override async ValueTask<
      InterceptionResult<int>
    > NonQueryExecutingAsync(
      DbCommand command,
      CommandEventData eventData,
      InterceptionResult<int> result,
      CancellationToken cancellationToken = default
    )
    {
      if (
        Action is { } action
        && command.CommandText.StartsWith("UPDATE \"ExecutionPlanningChanges\"")
      )
      {
        Action = null;
        await action();
      }
      return result;
    }
  }
}
