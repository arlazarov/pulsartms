using Microsoft.Extensions.Logging;

namespace Application.Features.Messaging.Services;

// A browser's subscription to MessagingEvents, kept between its requests so
// that it can ask "what changed?" with an ordinary request that ends.
// Firebase Hosting, which fronts the API in production, holds a streamed
// response back until it ends, so a server-sent stream never reached a
// browser; a request that answers as soon as a signal comes passes through.
//
// A signal raised between two requests waits in the mailbox's queue. A
// mailbox the caller does not own, or one that expired or lives on another
// instance, is replaced by a new one and the answer says "read everything
// again", as a reconnected stream did. Each mailbox belongs to one company
// and one account and lives Keep after its last request ends.
//
// One lock admits, leases, sweeps and evicts, so a mailbox is leased in
// the same step that finds it and only a mailbox nobody holds is ever
// closed. Three bounds, each refused (null, answered 503) rather than
// exceeded:
// - the process keeps at most Limit mailboxes, the one idle longest giving
//   way to a new one;
// - one account of one company keeps at most PerAccount, its own oldest
//   giving way, so one browser opening many cannot push out everyone
//   else's;
// - at most Held requests wait at once. Each holds one of the API
//   instance's request slots (80 on Cloud Run) for up to Wait, and the rest
//   of the API needs the others.
public sealed class MessagingMailboxes(
  MessagingEvents events,
  TimeProvider clock,
  ILogger<MessagingMailboxes> logger,
  int limit = MessagingMailboxes.Limit,
  int perAccount = MessagingMailboxes.PerAccount,
  int held = MessagingMailboxes.Held
)
{
  public static readonly TimeSpan Wait = TimeSpan.FromSeconds(20);
  public static readonly TimeSpan Keep = TimeSpan.FromSeconds(60);
  public const int Limit = 512;
  public const int PerAccount = 4;
  public const int Held = 40;

  private readonly object gate = new();
  private readonly Dictionary<Guid, Mailbox> mailboxes = [];

  // Guarded by the lock: requests holding a lease right now.
  private int holding;

  private sealed class Mailbox(
    Guid company,
    string account,
    MessagingEvents.Subscription subscription
  )
  {
    public Guid Company => company;
    public string Account => account;
    public MessagingEvents.Subscription Subscription => subscription;

    // One request reads the queue at a time: it has a single reader.
    public SemaphoreSlim Reading { get; } = new(1, 1);

    // Guarded by the lock. Requests holding or waiting for this mailbox;
    // while any does, it is neither swept nor evicted.
    public int Leases;
    public DateTimeOffset IdleSince;
  }

  public int Count
  {
    get
    {
      lock (gate)
        return mailboxes.Count;
    }
  }

  // Null: a bound admits nothing more.
  public async Task<MessagingChanges?> WaitAsync(
    Guid company,
    string account,
    Guid? id,
    CancellationToken ct
  )
  {
    Mailbox? mailbox = null;
    var known = Guid.Empty;
    Refusal? refused = null;
    lock (gate)
    {
      Sweep();
      if (
        id is not { } asked
        || !mailboxes.TryGetValue(asked, out var found)
        || found.Company != company
        || found.Account != account
      )
      {
        if (Open(company, account, out var bound) is { } opened)
          return new(opened, true, []);
        refused = Counted(bound, company, account);
      }
      else if (holding >= held)
        refused = Counted("waiting", company, account);
      else
      {
        (mailbox, known) = (found, asked);
        mailbox.Leases++;
        holding++;
      }
    }
    if (mailbox is null)
    {
      // Which bound refused, with the counts that decided it - never the
      // account - so a refusal in production can be attributed.
      logger.LogWarning(
        "Messaging mailbox refused by the {Bound} bound for {CompanyId}: "
          + "account mailboxes {AccountMailboxes}, held {AccountHeld}; "
          + "process mailboxes {Mailboxes}, waiting requests {Waiting}",
        refused!.Bound,
        company,
        refused.AccountMailboxes,
        refused.AccountHeld,
        refused.Mailboxes,
        refused.Waiting
      );
      return null;
    }
    try
    {
      return await ReadAsync(known, mailbox, ct);
    }
    finally
    {
      lock (gate)
      {
        holding--;
        if (--mailbox.Leases == 0)
          mailbox.IdleSince = clock.GetUtcNow();
      }
    }
  }

  private sealed record Refusal(
    string Bound,
    int AccountMailboxes,
    int AccountHeld,
    int Mailboxes,
    int Waiting
  );

  // Under the lock.
  private Refusal Counted(string bound, Guid company, string account)
  {
    var own = Own(company, account).ToList();
    return new(
      bound,
      own.Count,
      own.Count(x => x.Value.Leases > 0),
      mailboxes.Count,
      holding
    );
  }

  // Under the lock.
  private IEnumerable<KeyValuePair<Guid, Mailbox>> Own(
    Guid company,
    string account
  ) =>
    mailboxes.Where(x =>
      x.Value.Company == company && x.Value.Account == account
    );

  private async Task<MessagingChanges> ReadAsync(
    Guid id,
    Mailbox mailbox,
    CancellationToken ct
  )
  {
    using var timeout = new CancellationTokenSource(Wait, clock);
    using var wait = CancellationTokenSource.CreateLinkedTokenSource(
      ct,
      timeout.Token
    );
    try
    {
      await mailbox.Reading.WaitAsync(wait.Token);
    }
    // Another request of the same browser held the queue for the whole
    // wait: this one says nothing rather than compete for it.
    catch (OperationCanceledException) when (!ct.IsCancellationRequested)
    {
      return new(id, false, []);
    }
    try
    {
      var resync = mailbox.Subscription.TakeOverflow();
      var changed = Drain(mailbox);
      if (!resync && changed.Count == 0)
      {
        try
        {
          await mailbox.Subscription.Reader.WaitToReadAsync(wait.Token);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        { }
        resync = mailbox.Subscription.TakeOverflow();
        changed = Drain(mailbox);
      }
      return new(id, resync, changed);
    }
    finally
    {
      mailbox.Reading.Release();
    }
  }

  // Under the lock. The account's own oldest gives way at its share, then
  // the process's idlest at the limit; when those are all held, nothing is
  // admitted.
  private Guid? Open(Guid company, string account, out string bound)
  {
    var own = Own(company, account);
    bound = "account";
    if (own.Count() >= perAccount && !EvictIdlest(own))
      return null;
    bound = "process";
    if (mailboxes.Count >= limit && !EvictIdlest(mailboxes))
      return null;
    var id = Guid.NewGuid();
    mailboxes[id] = new(company, account, events.Subscribe(company))
    {
      IdleSince = clock.GetUtcNow(),
    };
    return id;
  }

  // Under the lock: closes the one of these idle longest, if any is idle.
  private bool EvictIdlest(IEnumerable<KeyValuePair<Guid, Mailbox>> among)
  {
    var idlest = among
      .Where(x => x.Value.Leases == 0)
      .OrderBy(x => x.Value.IdleSince)
      .Select(x => (Guid?)x.Key)
      .FirstOrDefault();
    if (idlest is not { } evicted)
      return false;
    Close(evicted);
    return true;
  }

  // Every change queued now, each conversation once; no waiting.
  private static List<Guid> Drain(Mailbox mailbox)
  {
    var changed = new List<Guid>();
    while (mailbox.Subscription.Reader.TryRead(out var change))
      if (
        change.ConversationId != Guid.Empty
        && !changed.Contains(change.ConversationId)
      )
        changed.Add(change.ConversationId);
    return changed;
  }

  // Under the lock.
  private void Sweep()
  {
    var before = clock.GetUtcNow() - Keep;
    foreach (
      var id in mailboxes
        .Where(x => x.Value.Leases == 0 && x.Value.IdleSince < before)
        .Select(x => x.Key)
        .ToList()
    )
      Close(id);
  }

  // Under the lock, and only for a mailbox nobody holds.
  private void Close(Guid id)
  {
    if (mailboxes.Remove(id, out var mailbox))
      mailbox.Subscription.Dispose();
  }
}

// What changed since the mailbox's last answer. Resync: read everything
// again, because this mailbox is new or signals were lost to a full queue.
public sealed record MessagingChanges(
  Guid Mailbox,
  bool Resync,
  IReadOnlyList<Guid> Conversations
);
