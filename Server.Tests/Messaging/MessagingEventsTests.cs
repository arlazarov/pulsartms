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

  [Fact]
  public async Task ConcurrentAcknowledgementsConsumeAnOverflowOnlyOnce()
  {
    var events = new MessagingEvents();
    using var subscription = events.Subscribe(Company.Amf);
    for (var i = 0; i <= MessagingEvents.QueueSize; i++)
      events.Publish(Company.Amf, new(Guid.NewGuid(), i));

    var start = new TaskCompletionSource(
      TaskCreationOptions.RunContinuationsAsynchronously
    );
    var readers = Enumerable
      .Range(0, 8)
      .Select(async _ =>
      {
        await start.Task;
        return subscription.TakeOverflow();
      })
      .ToArray();
    start.SetResult();

    Assert.Single(await Task.WhenAll(readers), value => value);
    events.Publish(Company.Amf, new(Guid.NewGuid(), 100));
    Assert.True(subscription.TakeOverflow());
    Assert.False(subscription.TakeOverflow());
  }

  [Fact]
  public void OverflowAndSignalsRemainCompanyScoped()
  {
    var events = new MessagingEvents();
    using var first = events.Subscribe(Company.Amf);
    using var other = events.Subscribe(Guid.NewGuid());
    for (var i = 0; i <= MessagingEvents.QueueSize; i++)
      events.Publish(Company.Amf, new(Guid.NewGuid(), i));

    Assert.True(first.TakeOverflow());
    Assert.False(other.TakeOverflow());
    Assert.False(other.Reader.TryRead(out _));
  }
}
