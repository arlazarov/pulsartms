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
  public void CompanyEntriesLiveOnlyWhileSubscribed()
  {
    var events = new MessagingEvents();
    for (var i = 0; i < 1000; i++)
    {
      using var subscription = events.Subscribe(Guid.NewGuid());
      Assert.Equal(1, events.CompanyCount);
    }
    Assert.Equal(0, events.CompanyCount);

    var old = events.Subscribe(Company.Amf);
    old.Dispose();
    using var replacement = events.Subscribe(Company.Amf);
    old.Dispose();
    events.Publish(Company.Amf, new(Guid.NewGuid(), 1));
    Assert.True(replacement.Reader.TryRead(out _));
    Assert.Equal(1, events.CompanyCount);
  }

  [Fact]
  public async Task LastReleaseAndNewSubscriptionCannotDetachTheNewReader()
  {
    var events = new MessagingEvents();
    var old = events.Subscribe(Company.Amf);
    using var start = new Barrier(2);
    var release = Task.Run(() =>
    {
      start.SignalAndWait();
      old.Dispose();
    });
    var joining = Task.Run(() =>
    {
      start.SignalAndWait();
      return events.Subscribe(Company.Amf);
    });
    await release;
    using var next = await joining;
    var change = new MessagingEvent(Guid.NewGuid(), 2);
    events.Publish(Company.Amf, change);
    Assert.True(next.Reader.TryRead(out var received));
    Assert.Equal(change, received);
    Assert.Equal(1, events.CompanyCount);
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
