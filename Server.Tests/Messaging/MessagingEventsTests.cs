using Application.Features.Messaging.Services;
using Domain.Entities;

namespace Server.Tests.Messaging;

// A subscriber that falls behind a burst: the signals that no longer fit
// its queue are not dropped silently, and it is told so once.
[Trait("Category", "Messaging")]
[Trait("Kind", "Unit")]
public sealed class MessagingEventsTests
{
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
