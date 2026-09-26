using System.Collections.Concurrent;
using System.Threading.Channels;

namespace Application.Features.Messaging.Services;

// "Conversation X changed" signals for the browsers of one company, raised
// only after the change committed. They carry no content: a browser reads
// what changed through the ordinary reads. Each subscriber has a small
// bounded queue. A signal that finds it full is not queued: the subscriber
// is told to read everything again before its next signal, so a burst
// never silently loses the change to the conversation a browser has open.
// Browsers subscribe through MessagingMailboxes. In this process only: a
// browser whose mailbox is on another instance hears of a change only
// through its own periodic repair (Client MessagingSignals.RepairEvery)
// until a fan-out exists.
public sealed class MessagingEvents
{
  public const int QueueSize = 64;

  private readonly ConcurrentDictionary<
    Guid,
    ConcurrentDictionary<Guid, Subscriber>
  > subscribers = new();

  private sealed class Subscriber(Channel<MessagingEvent> queue)
  {
    public Channel<MessagingEvent> Queue => queue;
    public int Overflowed;
  }

  public void Publish(Guid company, MessagingEvent change)
  {
    if (!subscribers.TryGetValue(company, out var channels))
      return;
    foreach (var channel in channels.Values)
      if (!channel.Queue.Writer.TryWrite(change))
        Interlocked.Exchange(ref channel.Overflowed, 1);
  }

  public Subscription Subscribe(Guid company)
  {
    var id = Guid.NewGuid();
    var subscriber = new Subscriber(
      Channel.CreateBounded<MessagingEvent>(
        new BoundedChannelOptions(QueueSize)
        {
          FullMode = BoundedChannelFullMode.Wait,
          SingleReader = true,
        }
      )
    );
    subscribers.GetOrAdd(company, _ => new())[id] = subscriber;
    return new(
      subscriber.Queue.Reader,
      () => Interlocked.Exchange(ref subscriber.Overflowed, 0) != 0,
      () =>
      {
        if (subscribers.TryGetValue(company, out var channels))
          channels.TryRemove(id, out _);
        subscriber.Queue.Writer.TryComplete();
      }
    );
  }

  public sealed class Subscription(
    ChannelReader<MessagingEvent> reader,
    Func<bool> overflowed,
    Action release
  ) : IDisposable
  {
    public ChannelReader<MessagingEvent> Reader => reader;

    // Whether signals were refused since the last ask: the subscriber must
    // read everything again.
    public bool TakeOverflow() => overflowed();

    public void Dispose() => release();
  }
}

public sealed record MessagingEvent(Guid ConversationId, long Revision);

// Wakes the outbox worker when a reply is queued, so it is sent at once
// rather than on the next poll.
public sealed class OutboxSignal
{
  private readonly SemaphoreSlim wake = new(0, 1);

  public void Wake()
  {
    if (wake.CurrentCount == 0)
      try
      {
        wake.Release();
      }
      catch (SemaphoreFullException) { }
  }

  public Task<bool> WaitAsync(TimeSpan timeout, CancellationToken ct) =>
    wake.WaitAsync(timeout, ct);
}
