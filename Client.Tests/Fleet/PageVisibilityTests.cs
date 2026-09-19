using Bunit;
using Client.Services;
using Microsoft.Extensions.Time.Testing;

namespace Client.Tests.Fleet;

[Trait("Category", "Fleet")]
[Trait("Kind", "Unit")]
public sealed class PageVisibilityTests
{
  [Fact]
  public void HiddenTabsPollAtTheSlowCadenceAndNeverFasterThanVisible()
  {
    using var context = new BunitContext();
    var visibility = new PageVisibility(context.JSInterop.JSRuntime);
    Assert.Equal(TimeSpan.FromSeconds(10), visibility.Interval(TimeSpan.FromSeconds(10)));
    visibility.OnVisibilityChanged(true);
    Assert.Equal(PageVisibilityPacing.HiddenInterval, visibility.Interval(TimeSpan.FromSeconds(10)));
    Assert.Equal(TimeSpan.FromMinutes(5), visibility.Interval(TimeSpan.FromMinutes(5)));
    visibility.OnVisibilityChanged(false);
    Assert.Equal(TimeSpan.FromSeconds(10), visibility.Interval(TimeSpan.FromSeconds(10)));
  }

  [Fact]
  public void PacingFollowsVisibilityChangesUntilDisposed()
  {
    using var context = new BunitContext();
    var visibility = new PageVisibility(context.JSInterop.JSRuntime);
    var clock = new FakeTimeProvider();
    using var timer = new PeriodicTimer(TimeSpan.FromSeconds(10), clock);
    var pace = visibility.Pace(timer, TimeSpan.FromSeconds(10));
    Assert.Equal(TimeSpan.FromSeconds(10), timer.Period);
    visibility.OnVisibilityChanged(true);
    Assert.Equal(TimeSpan.FromSeconds(60), timer.Period);
    visibility.OnVisibilityChanged(false);
    Assert.Equal(TimeSpan.FromSeconds(10), timer.Period);
    pace.Dispose();
    visibility.OnVisibilityChanged(true);
    Assert.Equal(TimeSpan.FromSeconds(10), timer.Period);
  }

  [Fact]
  public async Task RefreshLoopSlowsDownWhileHiddenAndResumesWhenVisible()
  {
    using var context = new BunitContext();
    var visibility = new PageVisibility(context.JSInterop.JSRuntime);
    var clock = new FakeTimeProvider();
    var refreshes = 0;
    using var refreshed = new SemaphoreSlim(0);
    using var stop = new CancellationTokenSource();
    var loop = RefreshLoop.RunAsync(_ => { refreshes++; refreshed.Release(); return Task.CompletedTask; }, _ => Task.CompletedTask,
      TimeSpan.FromSeconds(10), stop.Token, clock, visibility);
    await ExpectRefreshAsync(refreshed);
    Assert.Equal(1, refreshes);
    clock.Advance(TimeSpan.FromSeconds(10));
    await ExpectRefreshAsync(refreshed);
    Assert.Equal(2, refreshes);
    visibility.OnVisibilityChanged(true);
    clock.Advance(TimeSpan.FromSeconds(30));
    clock.Advance(TimeSpan.FromSeconds(30));
    await ExpectRefreshAsync(refreshed);
    // One hidden tick per minute: the earlier 30-second point produced nothing.
    Assert.Equal(3, refreshes);
    Assert.Equal(0, refreshed.CurrentCount);
    visibility.OnVisibilityChanged(false);
    clock.Advance(TimeSpan.FromSeconds(10));
    await ExpectRefreshAsync(refreshed);
    Assert.Equal(4, refreshes);
    stop.Cancel();
    await loop;
  }

  private static async Task ExpectRefreshAsync(SemaphoreSlim refreshed) =>
    Assert.True(await refreshed.WaitAsync(TimeSpan.FromSeconds(5)), "The loop did not refresh in time.");
}
