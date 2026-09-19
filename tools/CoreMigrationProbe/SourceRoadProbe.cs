using Application.Features.Routing.Models;
using Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;

namespace CoreMigrationProbe;

internal static class SourceRoadProbe
{
  private static readonly TimeSpan Lease = TimeSpan.FromMinutes(3);

  public static async Task VerifyAsync(AppDbContext db)
  {
    var options = new DbContextOptionsBuilder<AppDbContext>()
      .UseNpgsql(db.Database.GetConnectionString())
      .Options;
    await using var first = new AppDbContext(options);
    await using var second = new AppDbContext(options);
    var a = new SourceRoadStore(first);
    var b = new SourceRoadStore(second);
    var now = DateTime.UtcNow;
    var id = Guid.NewGuid();
    await Task.WhenAll(
      a.DemandAsync(id, "geometry", 0, now, default),
      b.DemandAsync(id, "geometry", 0, now, default)
    );
    Require(
      (await db.SourceRoadRequests.SingleAsync()).RequestedVersion == 1,
      "Concurrent map demand must coalesce into one durable request."
    );
    await a.ObserveAsync(id, null, "inputs", 1, false, false, now, default);
    var otherId = Guid.NewGuid();
    await b.ObserveAsync(otherId, null, "other", 1, false, false, now, default);
    SourceRoadWork old;
    SourceRoadWork other;
    await using (var transaction = await first.Database.BeginTransactionAsync())
    {
      old =
        await a.ClaimAsync(now, Lease, default)
        ?? throw new InvalidOperationException("First source road is missing.");
      using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
      other =
        await b.ClaimAsync(now, Lease, timeout.Token)
        ?? throw new InvalidOperationException(
          "Second source road is missing."
        );
      Require(
        old.DispatchId == id && other.DispatchId == otherId,
        "Source-road delivery must skip another worker's locked row."
      );
      await transaction.CommitAsync();
    }
    await b.ObserveAsync(id, null, "changed", 0, false, false, now, default);
    Require(
      await b.ClaimAsync(now, Lease, default) is null,
      "Changed source inputs must retain an existing live lease."
    );
    await a.CompleteAsync(old, true, now, now.AddHours(1), default);
    var changed = await b.ClaimAsync(now, Lease, default);
    Require(
      changed?.DispatchId == id
        && changed.Version == 2
        && changed.Attempts == 1,
      "Old source-road completion cannot consume a newer input version."
    );
    var retryAt = now.AddMinutes(1);
    await b.CompleteAsync(changed!, false, now, retryAt, default);
    await a.DemandAsync(id, "geometry", 0, now, default);
    await a.ObserveAsync(id, null, "changed", 0, false, true, now, default);
    Require(
      await a.ClaimAsync(retryAt.AddSeconds(-1), Lease, default) is null,
      "Polling and repair hints must preserve source-road retry deadlines."
    );
    var retry = await a.ClaimAsync(retryAt, Lease, default);
    Require(
      retry?.Version == 2 && retry.Attempts == 2,
      "Source-road retry must preserve its version and attempt count."
    );
    await a.CompleteAsync(retry!, true, retryAt, retryAt.AddHours(1), default);
    var expired = other.LeaseUntil;
    Require(
      !await b.CompleteAsync(other, true, expired, expired, default),
      "An expired source-road worker must not acknowledge work."
    );
    var recovered = await a.ClaimAsync(expired, Lease, default);
    Require(
      recovered?.DispatchId == otherId && recovered.LeaseId != other.LeaseId,
      "A new worker must recover abandoned source-road work."
    );
    Require(
      !await b.CompleteAsync(other, false, expired, expired, default)
        && await a.CompleteAsync(recovered!, true, expired, expired, default),
      "Only the replacement owner may acknowledge recovered work."
    );
    await b.DemandAsync(Guid.NewGuid(), "pending", 0, expired, default);
    var migrations = (await db.Database.GetAppliedMigrationsAsync()).ToArray();
    var rejected = false;
    try
    {
      await db.GetService<IMigrator>()
        .MigrateAsync("20260914214701_AddStopCorrections");
    }
    catch (PostgresException ex)
      when (ex.SqlState == "P0001"
        && ex.MessageText.StartsWith("Pending source roads require")
      )
    {
      rejected = true;
    }
    Require(
      rejected
        && migrations.SequenceEqual(
          await db.Database.GetAppliedMigrationsAsync()
        ),
      "Downgrade must retain source-road demand and migration history."
    );
    var last = await b.ClaimAsync(expired, Lease, default);
    await b.CompleteAsync(last!, true, expired, expired, default);
    Console.WriteLine(
      "Durable source roads: concurrent demand, skip-locked claims, retries, "
        + "lease recovery, version acknowledgement and downgrade guard passed."
    );
  }

  private static void Require(bool condition, string message)
  {
    if (!condition)
      throw new InvalidOperationException(message);
  }
}
