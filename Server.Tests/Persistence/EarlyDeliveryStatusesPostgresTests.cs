using Application.Features.Messaging.Services;
using Domain.Entities.Fleet;
using Domain.Entities.Messaging;
using Domain.Models.Messaging;
using Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Server.Tests.Support;

namespace Server.Tests.Persistence;

// Audit F27, Root's review: the owner's overlaps, each in two real
// transactions on PostgreSQL - a webhook keeping statuses against another
// webhook (the bound, the lock order) and against the sender saving the
// id, in both orders. The primitive lock alone proves none of these.
[Trait("Category", "Database")]
[Trait("Kind", "Integration")]
public sealed class EarlyDeliveryStatusesPostgresTests
{
  private const string Number = "123456";

  // The bound is counted under the carrier's admission lock: a second
  // webhook waits for the first's commit and then sees its rows.
  [RequiresPostgresFact]
  public async Task OverlappingWebhooksStayWithinTheBound()
  {
    await using var postgres = await PostgresFixture.CreateAsync();
    await using var first = postgres.Connect();
    await using var second = postgres.Connect();
    var waiting = await PidAsync(second);

    await using var one = await first.Database.BeginTransactionAsync();
    await Early(first, 2)
      .KeepAsync(Channel, Number, [Event("a"), Event("b")], default);
    await first.SaveChangesAsync();

    await using var two = await second.Database.BeginTransactionAsync();
    var keeping = Early(second, 2)
      .KeepAsync(Channel, Number, [Event("c")], default);
    await WaitsForALockAsync(postgres, waiting);
    Assert.False(keeping.IsCompleted);
    await one.CommitAsync();
    await keeping.WaitAsync(TimeSpan.FromSeconds(10));
    await second.SaveChangesAsync();
    await two.CommitAsync();

    await using var read = postgres.Connect();
    Assert.Equal(2, await read.PendingDeliveryStatuses.CountAsync());
  }

  // Batches naming the same messages in opposite orders complete, one
  // after the other, without a deadlock.
  [RequiresPostgresFact]
  public async Task BatchesInOppositeOrdersDoNotDeadlock()
  {
    await using var postgres = await PostgresFixture.CreateAsync();
    await using var first = postgres.Connect();
    await using var second = postgres.Connect();
    var waiting = await PidAsync(second);

    await using var one = await first.Database.BeginTransactionAsync();
    await Early(first)
      .KeepAsync(
        Channel,
        Number,
        [Event("b", "sent"), Event("a", "sent")],
        default
      );
    await first.SaveChangesAsync();
    await using var two = await second.Database.BeginTransactionAsync();
    var keeping = Early(second)
      .KeepAsync(
        Channel,
        Number,
        [Event("a", "delivered"), Event("b", "delivered")],
        default
      );
    await WaitsForALockAsync(postgres, waiting);
    await one.CommitAsync();
    await keeping.WaitAsync(TimeSpan.FromSeconds(10));
    await second.SaveChangesAsync();
    await two.CommitAsync();

    await using var read = postgres.Connect();
    Assert.Equal(4, await read.PendingDeliveryStatuses.CountAsync());
  }

  // The sender saves the id first and holds the message lock; the webhook
  // waits, then finds the id and applies the status - nothing is kept.
  [RequiresPostgresFact]
  public async Task TheSenderFirstThenTheWebhook()
  {
    await using var postgres = await PostgresFixture.CreateAsync();
    var id = await AttemptAsync(postgres);
    await using var sender = postgres.Connect();
    await using var webhook = postgres.Connect();
    var waiting = await PidAsync(webhook);

    await using var sending = await sender.Database.BeginTransactionAsync();
    var attempt = await sender.DriverMessages.SingleAsync(x => x.Id == id);
    Accept(attempt);
    await Early(sender).ApplyAsync(attempt, default);
    await sender.SaveChangesAsync();

    await using var receiving = await webhook.Database.BeginTransactionAsync();
    var keeping = Early(webhook)
      .KeepAsync(Channel, Number, [Failed()], default);
    await WaitsForALockAsync(postgres, waiting);
    Assert.False(keeping.IsCompleted);
    await sending.CommitAsync();
    await keeping.WaitAsync(TimeSpan.FromSeconds(10));
    await webhook.SaveChangesAsync();
    await receiving.CommitAsync();

    await AssertFailedAsync(postgres, id);
  }

  // The webhook keeps the status first and holds the message lock; the
  // sender waits, then takes and applies it.
  [RequiresPostgresFact]
  public async Task TheWebhookFirstThenTheSender()
  {
    await using var postgres = await PostgresFixture.CreateAsync();
    var id = await AttemptAsync(postgres);
    await using var sender = postgres.Connect();
    var waiting = await PidAsync(sender);
    await using var webhook = postgres.Connect();

    await using var receiving = await webhook.Database.BeginTransactionAsync();
    await Early(webhook).KeepAsync(Channel, Number, [Failed()], default);
    await webhook.SaveChangesAsync();

    await using var sending = await sender.Database.BeginTransactionAsync();
    var attempt = await sender.DriverMessages.SingleAsync(x => x.Id == id);
    Accept(attempt);
    var applying = Early(sender).ApplyAsync(attempt, default);
    await WaitsForALockAsync(postgres, waiting);
    Assert.False(applying.IsCompleted);
    await receiving.CommitAsync();
    await applying.WaitAsync(TimeSpan.FromSeconds(10));
    await sender.SaveChangesAsync();
    await sending.CommitAsync();

    await AssertFailedAsync(postgres, id);
  }

  // A release overlap: the previous binary saved the id and took nothing
  // - a kept "sent". Messaging's reconciliation overlaps a webhook holding
  // the same message's lock while it records "read": the reconciliation
  // waits for that commit and reads the message after it, so the kept
  // "sent" does not take it back. It holds one message lock at a time, as
  // a sender does, so it cannot close a cycle.
  [RequiresPostgresFact]
  public async Task ReconciliationWaitsForAWebhookOnTheSameMessage()
  {
    await using var postgres = await PostgresFixture.CreateAsync();
    var id = await AttemptAsync(postgres);
    await using (var keeping = postgres.Connect())
    {
      await using var kept = await keeping.Database.BeginTransactionAsync();
      await Early(keeping)
        .KeepAsync(Channel, Number, [Event("x", "sent")], default);
      await keeping.SaveChangesAsync();
      await kept.CommitAsync();
    }
    await using (var previous = postgres.Connect())
    {
      Accept(await previous.DriverMessages.SingleAsync(x => x.Id == id));
      await previous.SaveChangesAsync();
    }
    await using var webhook = postgres.Connect();
    await using var reconciler = postgres.Connect();
    var waiting = await PidAsync(reconciler);

    await using var receiving = await webhook.Database.BeginTransactionAsync();
    await Early(webhook)
      .KeepAsync(Channel, Number, [Event("x", "read")], default);
    await webhook.SaveChangesAsync();
    var reconciling = new KeptStatusReconciliation(
      reconciler,
      new DeliveryStatusLocks(reconciler),
      Early(reconciler),
      new TestCompany(),
      new MessagingEvents(),
      [],
      new KeptStatusRetries(),
      TimeProvider.System,
      NullLogger<KeptStatusReconciliation>.Instance
    ).RunOnceAsync(default);
    await WaitsForALockAsync(postgres, waiting);
    Assert.False(reconciling.IsCompleted);
    await receiving.CommitAsync();

    Assert.Equal(0, await reconciling.WaitAsync(TimeSpan.FromSeconds(10)));
    await using var read = postgres.Connect();
    Assert.Equal(
      DriverMessageStatuses.Read,
      (
        await read.DriverMessages.AsNoTracking().SingleAsync(x => x.Id == id)
      ).Status
    );
    Assert.Empty(
      await read.PendingDeliveryStatuses.AsNoTracking().ToListAsync()
    );
  }

  // An overlap is proven by the waiter's own wait for the lock, not by
  // time: opening a connection to a remote fixture can take longer than
  // any delay, which let a waiter that never waited look blocked. The
  // waiter's connection stays open, so its backend is the one watched.
  private static async Task<int> PidAsync(AppDbContext db)
  {
    await db.Database.OpenConnectionAsync();
    return await db
      .Database.SqlQuery<int>($"SELECT pg_backend_pid() AS \"Value\"")
      .SingleAsync();
  }

  private static async Task WaitsForALockAsync(
    PostgresFixture postgres,
    int pid
  )
  {
    await using var watch = postgres.Connect();
    var deadline = DateTime.UtcNow.AddSeconds(10);
    while (DateTime.UtcNow < deadline)
    {
      if (
        await watch
          .Database.SqlQuery<bool>(
            $"""
            SELECT EXISTS (
              SELECT 1 FROM pg_locks
              WHERE pid = {pid} AND locktype = 'advisory' AND NOT granted
            ) AS "Value"
            """
          )
          .SingleAsync()
      )
        return;
      await Task.Delay(20);
    }
    throw new TimeoutException("The overlap never waited for the lock.");
  }

  private const string Channel = DriverMessageChannels.WhatsApp;

  private static EarlyDeliveryStatuses Early(
    AppDbContext db,
    int bound = 1000
  ) =>
    new(
      db,
      new DeliveryStatusLocks(db),
      new TestCompany(),
      TimeProvider.System,
      bound
    );

  private static DriverMessageStatusEvent Event(
    string id,
    string status = "read"
  ) => new($"wamid.{id}", status, DateTime.UtcNow, null);

  private static DriverMessageStatusEvent Failed() =>
    new("wamid.x", DriverMessageStatuses.Failed, DateTime.UtcNow, 131047);

  private static void Accept(DriverMessage attempt)
  {
    attempt.ProviderMessageId = "wamid.x";
    attempt.Status = DriverMessageStatuses.Accepted;
    attempt.StatusAt = DateTime.UtcNow;
  }

  // An attempt being sent: recorded, no provider id yet.
  private static async Task<Guid> AttemptAsync(PostgresFixture postgres)
  {
    await using var db = postgres.Connect();
    var truck = new Truck
    {
      Id = Guid.NewGuid(),
      ExternalId = Guid.NewGuid().ToString("N"),
      UnitNumber = "54777",
      IsActive = true,
    };
    var driver = new Driver
    {
      Id = Guid.NewGuid(),
      ExternalId = Guid.NewGuid().ToString("N"),
      Name = "Driver",
      IsActive = true,
    };
    var attempt = new DriverMessage
    {
      Id = Guid.NewGuid(),
      DriverId = driver.Id,
      TruckId = truck.Id,
      DispatchId = Guid.NewGuid(),
      Channel = Channel,
      BusinessNumberId = Number,
      Recipient = "+15558234327",
      Text = "Fuel for this shift",
      IdempotencyKey = Guid.NewGuid().ToString("N"),
      Attempt = 1,
      Status = DriverMessageStatuses.Sending,
      StatusAt = DateTime.UtcNow,
      CreatedAt = DateTime.UtcNow,
    };
    db.AddRange(truck, driver, attempt);
    await db.SaveChangesAsync();
    return attempt.Id;
  }

  private static async Task AssertFailedAsync(PostgresFixture postgres, Guid id)
  {
    await using var read = postgres.Connect();
    var row = await read
      .DriverMessages.AsNoTracking()
      .SingleAsync(x => x.Id == id);
    Assert.Equal(
      ("wamid.x", DriverMessageStatuses.Failed, (int?)131047),
      (row.ProviderMessageId, row.Status, row.ErrorCode)
    );
    Assert.Empty(
      await read.PendingDeliveryStatuses.AsNoTracking().ToListAsync()
    );
  }
}
