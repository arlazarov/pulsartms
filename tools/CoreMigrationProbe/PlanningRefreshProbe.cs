using Application.Features.Routing.Models;
using Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;

namespace CoreMigrationProbe;

internal static class PlanningRefreshProbe
{
  private static readonly TimeSpan Lease = TimeSpan.FromMinutes(4);

  public static async Task VerifyAsync(AppDbContext db)
  {
    var options = new DbContextOptionsBuilder<AppDbContext>()
      .UseNpgsql(db.Database.GetConnectionString())
      .Options;
    var now = DateTime.UtcNow;
    var source = new PlanningScope(Guid.NewGuid(), null);
    await using var first = new AppDbContext(options);
    await using var second = new AppDbContext(options);
    var a = new PlanningRefreshStore(first);
    var b = new PlanningRefreshStore(second);
    await Task.WhenAll(
      a.RequestAsync(source, "first", now, default),
      b.RequestAsync(source, "first", now, default)
    );
    Require(
      (await db.PlanningRefreshRequests.SingleAsync()).RequestedVersion == 1,
      "Concurrent identical demand must create exactly one request version."
    );
    var native = new PlanningScope(Guid.NewGuid(), Guid.NewGuid(), 4);
    await b.RequestAsync(native, "native", now.AddSeconds(1), default);
    now = now.AddSeconds(2);
    PlanningRefreshWork old;
    PlanningRefreshWork other;
    await using (var tx = await first.Database.BeginTransactionAsync())
    {
      old =
        await a.ClaimAsync(now, Lease, default)
        ?? throw new InvalidOperationException("First claim is missing.");
      using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
      other =
        await b.ClaimAsync(now, Lease, timeout.Token)
        ?? throw new InvalidOperationException("Concurrent claim is missing.");
      Require(
        old.Scope == source && other.Scope == native,
        "Independent workers must claim different unlocked requests."
      );
      await tx.CommitAsync();
    }
    await b.RequestAsync(source, "changed", now, default);
    Require(
      await b.ClaimAsync(now, Lease, default) is null,
      "New input must retain the existing worker's live lease."
    );
    Require(
      await a.CompleteAsync(old, true, now, now.AddHours(1), default),
      "The original worker must acknowledge its own attempt."
    );
    var latest = await b.ClaimAsync(now, Lease, default);
    Require(
      latest?.Scope == source && latest.Version == 2 && latest.Attempts == 1,
      "Older completion must leave newer input immediately eligible."
    );
    var retryAt = now.AddMinutes(1);
    await b.CompleteAsync(latest!, false, now, retryAt, default);
    var repeated = await a.RequestAsync(source, "changed", now, default);
    Require(
      repeated.Pending
        && repeated.AvailableAt == retryAt
        && await a.ClaimAsync(retryAt.AddSeconds(-1), Lease, default) is null,
      "Repeated demand must not bypass durable failure backoff."
    );
    var retry = await a.ClaimAsync(retryAt, Lease, default);
    Require(
      retry?.Version == 2 && retry.Attempts == 2,
      "A due retry must preserve the request version."
    );
    await a.CompleteAsync(
      retry!,
      true,
      retryAt,
      retryAt.AddMinutes(2),
      default
    );
    Require(
      !(await b.RequestAsync(source, "changed", retryAt, default)).Pending,
      "Success cooldown must be visible to another connection."
    );
    var expired = other.LeaseUntil;
    Require(
      !await b.CompleteAsync(other, true, expired, expired, default),
      "An expired worker must not acknowledge its old lease."
    );
    var recovered = await a.ClaimAsync(expired, Lease, default);
    Require(
      recovered?.Scope == native && recovered.LeaseId != other.LeaseId,
      "Another process must recover abandoned work."
    );
    Require(
      !await b.CompleteAsync(other, false, expired, expired, default)
        && await a.CompleteAsync(recovered!, true, expired, expired, default),
      "Only the current lease may complete recovered work."
    );
    var pending = new PlanningScope(Guid.NewGuid(), null);
    await a.RequestAsync(pending, "pending", expired, default);
    var migrations = (await db.Database.GetAppliedMigrationsAsync()).ToArray();
    var rejected = false;
    try
    {
      await db.GetService<IMigrator>()
        .MigrateAsync("20260914214701_AddStopCorrections");
    }
    catch (PostgresException ex)
      when (ex.SqlState == "P0001"
        && ex.MessageText.StartsWith("Pending planning requires")
      )
    {
      rejected = true;
    }
    Require(
      rejected
        && migrations.SequenceEqual(
          await db.Database.GetAppliedMigrationsAsync()
        ),
      "Downgrade must not discard pending requests or migration history."
    );
    var last = await b.ClaimAsync(expired, Lease, default);
    Require(
      last?.Scope == pending,
      "Pending demand must survive rejected downgrade."
    );
    await b.CompleteAsync(last!, true, expired, expired, default);
    Require(
      !await db.PlanningRefreshRequests.AnyAsync(x =>
        x.RequestedVersion > x.CompletedVersion
      ),
      "Every fixture request must finish before the remaining downgrade checks."
    );
    Console.WriteLine(
      "PostgreSQL planning refresh passed: concurrent demand coalescing, "
        + "nonblocking claims, new input during work, durable retry/cooldown, "
        + "lease recovery and pending-work downgrade protection."
    );
  }

  private static void Require(bool condition, string message)
  {
    if (!condition)
      throw new InvalidOperationException(message);
  }
}
