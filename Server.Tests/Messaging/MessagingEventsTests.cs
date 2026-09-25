using Application.Features.Messaging.Queries;
using Application.Features.Messaging.Services;
using Domain.Entities;

namespace Server.Tests.Messaging;

// A browser's stream that falls behind a burst: the signals that no longer
// fit its queue are not dropped silently. It is told to read everything
// again before the signals that did queue, so the change to the
// conversation it has open is never lost.
[Trait("Category", "Messaging")]
[Trait("Kind", "Unit")]
public sealed class MessagingEventsTests
{
  [Fact]
  public async Task AFullQueueIsFollowedByAResyncBeforeWhatQueued()
  {
    var events = new MessagingEvents();
    var handler = new ConversationHandlers(
      null!,
      null!,
      new TestCompany(),
      events,
      TimeProvider.System
    );
    await using var stream = handler
      .Handle(new StreamMessagingEventsQuery(), default)
      .GetAsyncEnumerator();
    var first = stream.MoveNextAsync();
    events.Publish(Company.Amf, new(Guid.NewGuid(), 1));
    Assert.True(await first);

    var burst = Enumerable
      .Range(0, MessagingEvents.QueueSize + 6)
      .Select(i => new MessagingEvent(Guid.NewGuid(), i + 2))
      .ToList();
    foreach (var change in burst)
      events.Publish(Company.Amf, change);

    Assert.True(await stream.MoveNextAsync());
    Assert.Equal(MessagingEvent.Resync, stream.Current);
    Assert.True(await stream.MoveNextAsync());
    Assert.Equal(burst[0], stream.Current);
  }

  [Fact]
  public void AQueueThatKeepsUpIsNeverToldToResync()
  {
    var events = new MessagingEvents();
    using var subscription = events.Subscribe(Company.Amf);

    for (var i = 0; i < MessagingEvents.QueueSize; i++)
      events.Publish(Company.Amf, new(Guid.NewGuid(), i));

    Assert.False(subscription.TakeOverflow());
    events.Publish(Company.Amf, new(Guid.NewGuid(), 99));
    Assert.True(subscription.TakeOverflow());
    Assert.False(subscription.TakeOverflow());
  }
}
