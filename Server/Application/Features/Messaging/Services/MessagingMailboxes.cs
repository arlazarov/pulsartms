using System.Collections.Concurrent;

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
// and one account, lives Keep after its last answer, and the process keeps
// at most Limit of them, dropping the one idle longest.
public sealed class MessagingMailboxes(
  MessagingEvents events,
  TimeProvider clock
)
{
  public static readonly TimeSpan Wait = TimeSpan.FromSeconds(20);
  public static readonly TimeSpan Keep = TimeSpan.FromSeconds(60);
  public const int Limit = 512;

  private readonly ConcurrentDictionary<Guid, Mailbox> mailboxes = new();

  private sealed class Mailbox(
    Guid company,
    string account,
    MessagingEvents.Subscription subscription
  )
  {
    public Guid Company => company;
    public string Account => account;
    public MessagingEvents.Subscription Subscription => subscription;

    // One request reads a mailbox at a time: its queue has a single reader.
    public SemaphoreSlim Reading { get; } = new(1, 1);

    // Null while a request is reading it: a mailbox in use never expires.
    public DateTimeOffset? IdleSince;
  }

  public int Count => mailboxes.Count;

  public async Task<MessagingChanges> WaitAsync(
    Guid company,
    string account,
    Guid? id,
    CancellationToken ct
  )
  {
    Sweep();
    if (
      id is not { } known
      || !mailboxes.TryGetValue(known, out var mailbox)
      || mailbox.Company != company
      || mailbox.Account != account
    )
      return new(Open(company, account), true, []);
    using var timeout = new CancellationTokenSource(Wait, clock);
    using var wait = CancellationTokenSource.CreateLinkedTokenSource(
      ct,
      timeout.Token
    );
    try
    {
      // Another request of the same browser may be reading it: this one
      // waits its turn within the same limit, then says nothing rather than
      // competing for the queue.
      await mailbox.Reading.WaitAsync(wait.Token);
    }
    catch (OperationCanceledException) when (!ct.IsCancellationRequested)
    {
      return new(known, false, []);
    }
    try
    {
      mailbox.IdleSince = null;
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
      return new(known, resync, changed);
    }
    finally
    {
      mailbox.IdleSince = clock.GetUtcNow();
      mailbox.Reading.Release();
    }
  }

  private Guid Open(Guid company, string account)
  {
    if (mailboxes.Count >= Limit)
      DropIdlest();
    var id = Guid.NewGuid();
    mailboxes[id] = new(company, account, events.Subscribe(company))
    {
      IdleSince = clock.GetUtcNow(),
    };
    return id;
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

  private void Sweep()
  {
    var before = clock.GetUtcNow() - Keep;
    foreach (var (id, mailbox) in mailboxes)
      if (mailbox.IdleSince is { } idle && idle < before)
        Close(id);
  }

  // Every mailbox being read: the limit gives way rather than wait.
  private void DropIdlest()
  {
    if (
      mailboxes
        .Where(x => x.Value.IdleSince is not null)
        .OrderBy(x => x.Value.IdleSince)
        .Select(x => (Guid?)x.Key)
        .FirstOrDefault() is
      { } id
    )
      Close(id);
  }

  private void Close(Guid id)
  {
    if (mailboxes.TryRemove(id, out var mailbox))
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
