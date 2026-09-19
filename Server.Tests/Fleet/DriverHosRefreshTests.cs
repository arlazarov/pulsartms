using Application.Features.Fleet.Background;
using Application.Features.Fleet.Interfaces;
using Application.Features.Fleet.Models;
using Application.Features.Fleet.Services;
using Application.Features.Synchronization.Interfaces;
using Application.Features.Synchronization.Models;
using Application.Features.Synchronization.Options;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using SynchronizationSnapshot = Application.Features.Synchronization.Models.SynchronizationStatus;

namespace Server.Tests.Fleet;

[Trait("Category", "Fleet")]
[Trait("Kind", "Integration")]
public sealed class DriverHosRefreshTests
{
  [Fact]
  public async Task RefreshIsCoalescedAndSlowProviderDoesNotBlockReaders()
  {
    var time = new ManualTimeProvider();
    var snapshot = new DriverHosSnapshot(time);
    var refresh = new RefreshProvider();
    await using var services = new ServiceCollection()
      .AddSingleton<IDriverHosRefreshProvider>(refresh)
      .BuildServiceProvider();
    var operation = new DriverHosRefreshOperation(
      snapshot,
      services.GetRequiredService<IServiceScopeFactory>(),
      Options.Create(new SynchronizationOptions { Enabled = true }),
      new SynchronizationStatus(true),
      NullLogger<DriverHosRefreshOperation>.Instance
    );
    var pending = operation.RunOnceAsync(default);
    await refresh.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
    try
    {
      Assert.Empty(
        await snapshot
          .GetClocksAsync(default)
          .WaitAsync(TimeSpan.FromSeconds(1))
      );
      await operation.RunOnceAsync(default);
      Assert.Equal(1, refresh.Calls);
    }
    finally
    {
      refresh.Release.SetResult(
        new Dictionary<string, DriverHosClocks>
        {
          ["driver"] = new() { UpdatedAt = time.GetUtcNow().UtcDateTime },
        }
      );
    }
    await pending;
    Assert.Single(await snapshot.GetClocksAsync(default));
    await operation.RunOnceAsync(default);
    Assert.Equal(1, refresh.Calls);
  }

  [Fact]
  public async Task AnIdleNonOwnerDoesNotStartAnotherProactivePoller()
  {
    var time = new ManualTimeProvider();
    var snapshot = new DriverHosSnapshot(time);
    var refresh = new RefreshProvider();
    await using var services = new ServiceCollection()
      .AddSingleton<IDriverHosRefreshProvider>(refresh)
      .BuildServiceProvider();
    var operation = new DriverHosRefreshOperation(
      snapshot,
      services.GetRequiredService<IServiceScopeFactory>(),
      Options.Create(new SynchronizationOptions { Enabled = true }),
      new SynchronizationStatus(false),
      NullLogger<DriverHosRefreshOperation>.Instance
    );
    await operation.RunOnceAsync(default);
    Assert.Equal(0, refresh.Calls);
  }

  private sealed class SynchronizationStatus(bool active)
    : ISynchronizationStatusProvider
  {
    public SynchronizationSnapshot Status => new(true, active, 0, []);
  }

  private sealed class RefreshProvider : IDriverHosRefreshProvider
  {
    public int Calls;
    public TaskCompletionSource Entered { get; } =
      new(TaskCreationOptions.RunContinuationsAsynchronously);
    public TaskCompletionSource<
      IReadOnlyDictionary<string, DriverHosClocks>
    > Release { get; } =
      new(TaskCreationOptions.RunContinuationsAsynchronously);

    public Task<
      IReadOnlyDictionary<string, DriverHosClocks>
    > RefreshClocksAsync(CancellationToken ct)
    {
      Calls++;
      Entered.TrySetResult();
      return Release.Task.WaitAsync(ct);
    }
  }
}
