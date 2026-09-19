using System.Threading.Channels;
using Client.Services;
using Microsoft.Extensions.Time.Testing;

namespace Client.Tests.Fleet;

[Trait("Category", "Fleet")]
[Trait("Kind", "Unit")]
public sealed class PageVisibilityTests
{
  [Fact]
  public async Task ResumeDuringAnInFlightRequestDiscardsItsOldPendingTimerTick()
  {
    var clock = new FakeTimeProvider();
    await using var visibility = new PageVisibility();
    using var lifetime = new CancellationTokenSource();
    var calls = Channel.CreateUnbounded<int>();
    var release = new TaskCompletionSource(
      TaskCreationOptions.RunContinuationsAsynchronously
    );
    var count = 0;
    var running = RefreshLoop.RunAsync(
      async ct =>
      {
        var current = ++count;
        calls.Writer.TryWrite(current);
        if (current == 1)
          await release.Task.WaitAsync(ct);
      },
      _ => Task.CompletedTask,
      TimeSpan.FromSeconds(10),
      lifetime.Token,
      clock,
      visibility
    );
    try
    {
      Assert.Equal(1, await calls.Reader.ReadAsync());
      visibility.VisibilityChanged(false);
      clock.Advance(TimeSpan.FromMinutes(3));
      visibility.VisibilityChanged(true);
      release.SetResult();
      Assert.Equal(
        2,
        await calls
          .Reader.ReadAsync()
          .AsTask()
          .WaitAsync(TimeSpan.FromSeconds(5))
      );
      clock.Advance(TimeSpan.FromSeconds(9));
      await Task.Yield();
      Assert.Equal(2, count);
      clock.Advance(TimeSpan.FromSeconds(1));
      Assert.Equal(
        3,
        await calls
          .Reader.ReadAsync()
          .AsTask()
          .WaitAsync(TimeSpan.FromSeconds(5))
      );
    }
    finally
    {
      lifetime.Cancel();
      await running;
    }
  }

  [Fact]
  public async Task FastVisibleResumeRefreshesOnceAndRestartsThePollingInterval()
  {
    var clock = new FakeTimeProvider();
    await using var visibility = new PageVisibility();
    using var lifetime = new CancellationTokenSource();
    var calls = Channel.CreateUnbounded<int>();
    var count = 0;
    var running = RefreshLoop.RunAsync(
      _ =>
      {
        calls.Writer.TryWrite(++count);
        return Task.CompletedTask;
      },
      _ => Task.CompletedTask,
      TimeSpan.FromSeconds(10),
      lifetime.Token,
      clock,
      visibility
    );
    try
    {
      Assert.Equal(1, await calls.Reader.ReadAsync());
      visibility.VisibilityChanged(false);
      clock.Advance(TimeSpan.FromMinutes(3));
      Assert.Equal(1, count);
      visibility.VisibilityChanged(true);
      Assert.Equal(
        2,
        await calls
          .Reader.ReadAsync()
          .AsTask()
          .WaitAsync(TimeSpan.FromSeconds(5))
      );
      clock.Advance(TimeSpan.FromSeconds(9));
      await Task.Yield();
      Assert.Equal(2, count);
      clock.Advance(TimeSpan.FromSeconds(1));
      Assert.Equal(
        3,
        await calls
          .Reader.ReadAsync()
          .AsTask()
          .WaitAsync(TimeSpan.FromSeconds(5))
      );
    }
    finally
    {
      lifetime.Cancel();
      await running;
    }
  }

  [Fact]
  public async Task HiddenPollingStopsAndVisibleResumeCoalescesWithoutOverlappingRequests()
  {
    var clock = new FakeTimeProvider();
    await using var visibility = new PageVisibility();
    using var lifetime = new CancellationTokenSource();
    var calls = Channel.CreateUnbounded<int>();
    var release = new TaskCompletionSource(
      TaskCreationOptions.RunContinuationsAsynchronously
    );
    var count = 0;
    var running = RefreshLoop.RunAsync(
      async ct =>
      {
        var current = ++count;
        calls.Writer.TryWrite(current);
        if (current == 2)
          await release.Task.WaitAsync(ct);
      },
      _ => Task.CompletedTask,
      TimeSpan.FromSeconds(10),
      lifetime.Token,
      clock,
      visibility
    );
    Assert.Equal(1, await calls.Reader.ReadAsync());
    visibility.VisibilityChanged(false);
    clock.Advance(TimeSpan.FromMinutes(3));
    await Task.Yield();
    Assert.Equal(1, count);
    visibility.VisibilityChanged(true);
    Assert.Equal(
      2,
      await calls.Reader.ReadAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5))
    );
    visibility.VisibilityChanged(true);
    clock.Advance(TimeSpan.FromMinutes(3));
    Assert.Equal(2, count);
    lifetime.Cancel();
    release.SetResult();
    await running;
    Assert.Equal(2, count);
  }
}
