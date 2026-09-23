using Application.Diagnostics.Consistency;
using Application.Features.Execution.Audit;
using Domain.Entities;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Server.Tests.Persistence;

// The journal's writers of one company take a PostgreSQL advisory lock
// before they read. A second writer therefore waits for the first to
// commit, and journal sequences commit in the order readers see them: a
// reader never finds a later sequence before an earlier one has committed.
[Trait("Category", "Database")]
[Trait("Kind", "Integration")]
public sealed class ConsistencyJournalPostgresTests
{
  [RequiresPostgresFact]
  public async Task ASecondWriterWaitsAndSequencesCommitInOrder()
  {
    await using var fixture = await PostgresFixture.CreateAsync();
    var hold = new HoldBeforeCommit();
    var rule = new ExecutionPlanningDemandRule(null!).Info;
    var at = new DateTime(2026, 9, 23, 12, 0, 0, DateTimeKind.Utc);
    ConsistencyObservation Seen(string key) =>
      new(key, "v", new Dictionary<string, string>());

    var early = new ConsistencyJournal(fixture.Connect(hold)).RecordAsync(
      Company.Amf,
      Guid.NewGuid(),
      rule,
      at,
      [Seen("early")],
      default
    );
    await hold.Reached.Task.WaitAsync(TimeSpan.FromSeconds(30));
    var late = new ConsistencyJournal(fixture.Connect()).RecordAsync(
      Company.Amf,
      Guid.NewGuid(),
      rule,
      at,
      [Seen("late")],
      default
    );
    await Task.Delay(1_000);
    Assert.False(late.IsCompleted, late.Exception?.ToString());
    var reads = new ConsistencyJournalReads(fixture.Connect());
    Assert.Empty(await reads.EventsAsync(Company.Amf, 0, 10, null, default));

    hold.Release.SetResult();
    await Task.WhenAll(early, late);

    Assert.Equal(
      [(1L, "early"), (2L, "late")],
      (await reads.EventsAsync(Company.Amf, 0, 10, null, default)).Select(x =>
        (x.Sequence, x.EntityKey)
      )
    );
  }

  private sealed class HoldBeforeCommit : SaveChangesInterceptor
  {
    public TaskCompletionSource Reached { get; } =
      new(TaskCreationOptions.RunContinuationsAsynchronously);
    public TaskCompletionSource Release { get; } =
      new(TaskCreationOptions.RunContinuationsAsynchronously);

    public override async ValueTask<int> SavedChangesAsync(
      SaveChangesCompletedEventData eventData,
      int result,
      CancellationToken cancellationToken = default
    )
    {
      Reached.TrySetResult();
      await Release.Task;
      return result;
    }
  }
}
