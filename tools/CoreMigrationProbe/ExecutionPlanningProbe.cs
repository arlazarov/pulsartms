using Domain.Entities.Execution;
using Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace CoreMigrationProbe;

internal static class ExecutionPlanningProbe
{
  public static async Task VerifyAsync(AppDbContext db)
  {
    var options = new DbContextOptionsBuilder<AppDbContext>()
      .UseNpgsql(db.Database.GetConnectionString())
      .Options;
    var now = DateTime.UtcNow;
    var earlier = Request(now.AddSeconds(-1));
    var later = Request(now);
    db.ExecutionPlanningChanges.AddRange(earlier, later);
    await db.SaveChangesAsync();
    await using var first = new AppDbContext(options);
    await using var second = new AppDbContext(options);
    var firstStore = new ExecutionPlanningStore(first);
    var secondStore = new ExecutionPlanningStore(second);
    await using (var transaction = await first.Database.BeginTransactionAsync())
    {
      var claimed = await firstStore.ClaimAsync(now, default);
      Require(
        claimed?.Id == earlier.Id,
        "Claim must select the first due item."
      );
      using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
      var concurrent = await secondStore.ClaimAsync(now, timeout.Token);
      Require(
        concurrent?.Id == later.Id,
        "Another owner must skip a locked claim without waiting."
      );
      later = concurrent!;
      Require(
        await firstStore.CompleteAsync(claimed!, now, true, default),
        "The live owner must be able to acknowledge its own work."
      );
      await transaction.CommitAsync();
    }
    Require(
      await secondStore.ClaimAsync(now, default) is null,
      "Completed and actively leased requests must not be claimed."
    );
    var expiredAt = later.LeaseUntil!.Value;
    Require(
      !await secondStore.CompleteAsync(later, expiredAt, true, default),
      "An expired owner must not acknowledge work before recovery."
    );
    await using var restarted = new AppDbContext(options);
    var recovery = new ExecutionPlanningStore(restarted);
    var current = await recovery.ClaimAsync(expiredAt, default);
    Require(
      current?.Id == later.Id
        && current.Attempts == 2
        && current.LeaseId != later.LeaseId,
      "A fresh process must recover abandoned work with a new lease."
    );
    Require(
      !await secondStore.CompleteAsync(later, expiredAt, false, default),
      "The former owner must not reschedule the replacement owner's work."
    );
    Require(
      await recovery.CompleteAsync(current!, expiredAt, false, default),
      "The current owner must be able to retain a failed request for retry."
    );
    Require(
      await recovery.ClaimAsync(expiredAt.AddSeconds(59), default) is null,
      "Retry backoff must survive context replacement."
    );
    var retryAt = expiredAt.AddMinutes(1);
    var retry = await firstStore.ClaimAsync(retryAt, default);
    Require(
      retry?.Id == later.Id && retry.Attempts == 3,
      "Retry must become available at its durable deadline."
    );
    var newer = Request(retryAt);
    newer.ExecutionLegId = later.ExecutionLegId;
    newer.DispatchId = later.DispatchId;
    newer.TruckId = later.TruckId;
    newer.AssignmentRevision = later.AssignmentRevision + 1;
    db.ExecutionPlanningChanges.Add(newer);
    await db.SaveChangesAsync();
    Require(
      !await recovery.CompleteAsync(newer, retryAt, true, default),
      "An unclaimed request must not be acknowledged."
    );
    Require(
      await firstStore.CompleteAsync(retry!, retryAt, true, default),
      "Retry completion must acknowledge only the captured request."
    );
    var next = await recovery.ClaimAsync(retryAt, default);
    Require(
      next?.Id == newer.Id && next.AssignmentRevision == 2,
      "Completing older work must leave a newer assignment request pending."
    );
    Require(
      await recovery.CompleteAsync(next!, retryAt, true, default)
        && !await recovery.CompleteAsync(next!, retryAt, false, default),
      "Duplicate completion must not reopen completed work."
    );
    Console.WriteLine(
      "PostgreSQL execution queue passed: concurrent nonblocking claims, "
        + "restart recovery, expired/stale-owner rejection, durable retry "
        + "and independent request acknowledgements."
    );
  }

  private static ExecutionPlanningChange Request(DateTime now) =>
    new()
    {
      DispatchId = Guid.NewGuid(),
      TruckId = Guid.NewGuid(),
      ExecutionLegId = Guid.NewGuid(),
      AssignmentRevision = 1,
      RequestedAt = now,
      AvailableAt = now,
    };

  private static void Require(bool condition, string message)
  {
    if (!condition)
      throw new InvalidOperationException(message);
  }
}
