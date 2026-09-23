using System.Collections.Concurrent;
using System.Threading.Channels;

namespace Application.Features.Routing.Services.Messaging;

// "Conversation X changed" signals for the browsers of one company, raised
// only after the change committed. They carry no content: a browser reads
// what changed through the ordinary reads. Each subscriber has a small
// bounded queue that drops its oldest signals when full, since any later
// signal makes it read again anyway. In this process only; a second
// instance would need a fan-out first.
public sealed class MessagingEvents
{
  public const int QueueSize = 64;

  private readonly ConcurrentDictionary<
    Guid,
    ConcurrentDictionary<Guid, Channel<MessagingEvent>>
  > subscribers = new();

  public void Publish(Guid company, MessagingEvent change)
  {
    if (!subscribers.TryGetValue(company, out var channels))
      return;
    foreach (var channel in channels.Values)
      channel.Writer.TryWrite(change);
  }

  public Subscription Subscribe(Guid company)
  {
    var id = Guid.NewGuid();
    var channel = Channel.CreateBounded<MessagingEvent>(
      new BoundedChannelOptions(QueueSize)
      {
        FullMode = BoundedChannelFullMode.DropOldest,
        SingleReader = true,
      }
    );
    subscribers.GetOrAdd(company, _ => new())[id] = channel;
    return new(
      channel.Reader,
      () =>
      {
        if (subscribers.TryGetValue(company, out var channels))
          channels.TryRemove(id, out _);
        channel.Writer.TryComplete();
      }
    );
  }

  public sealed class Subscription(
    ChannelReader<MessagingEvent> reader,
    Action release
  ) : IDisposable
  {
    public ChannelReader<MessagingEvent> Reader => reader;

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
