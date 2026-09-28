using System.Text.RegularExpressions;
using Application.Diagnostics;
using Infrastructure.Diagnostics;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Server.Tests.Support;

namespace Server.Tests.Architecture;

// Root's decision: per-operation progress reports stale work and never
// restarts the only instance. Periodic work is stale when no round started
// for three intervals (at least five minutes); on-demand work only while a
// round runs past its limit, never for waiting. Stale is degraded
// readiness, not liveness.
[Trait("Category", "Architecture")]
[Trait("Kind", "Unit")]
public sealed class BackgroundProgressTests
{
  private static DateTimeOffset Now => DateTimeOffset.UtcNow;

  [Fact]
  public void PeriodicWorkIsStaleOnlyWhenNoRoundStartedForItsLimit()
  {
    var name = Name();
    BackgroundProgress.Expect(name, TimeSpan.FromMinutes(2));
    try
    {
      var start = Now;
      BackgroundProgress.Started(name, start);

      Assert.False(Of(name, start.AddMinutes(5.9)).Stale);
      Assert.True(Of(name, start.AddMinutes(6.1)).Stale);
      BackgroundProgress.Started(name, start.AddMinutes(6.1));
      Assert.False(Of(name, start.AddMinutes(6.2)).Stale);
      Assert.Equal(0, Of(name, start).Running);
    }
    finally
    {
      BackgroundProgress.Forget(name);
    }
  }

  [Fact]
  public void AFastLoopIsGivenAtLeastFiveMinutes()
  {
    var name = Name();
    BackgroundProgress.Expect(name, TimeSpan.FromSeconds(5));
    try
    {
      var start = Now;
      BackgroundProgress.Started(name, start);
      Assert.False(Of(name, start.AddMinutes(4.9)).Stale);
      Assert.True(Of(name, start.AddMinutes(5.1)).Stale);
    }
    finally
    {
      BackgroundProgress.Forget(name);
    }
  }

  [Fact]
  public void OnDemandWorkIsNeverStaleForWaitingOnlyForARoundThatRunsTooLong()
  {
    var name = Name();
    BackgroundProgress.OnDemand(name, TimeSpan.FromMinutes(5));
    try
    {
      var start = Now;
      Assert.False(Of(name, start.AddDays(3)).Stale);

      BackgroundProgress.Started(name, start);
      Assert.False(Of(name, start.AddMinutes(4.9)).Stale);
      Assert.True(Of(name, start.AddMinutes(5.1)).Stale);
      BackgroundProgress.Finished(name, start.AddMinutes(6));
      Assert.False(Of(name, start.AddDays(3)).Stale);
    }
    finally
    {
      BackgroundProgress.Forget(name);
    }
  }

  // Stale progress degrades readiness and leaves liveness alone.
  [Fact]
  public async Task StaleProgressIsDegradedReadinessNotLiveness()
  {
    var name = Name();
    BackgroundProgress.Expect(name, TimeSpan.FromSeconds(5));
    try
    {
      var later = new ManualTimeProvider(Now.AddHours(1));
      var state = new BackgroundState();
      var ready = await new BackgroundProgressHealthCheck(
        state,
        later
      ).CheckHealthAsync(new());
      var live = await new BackgroundWorkHealthCheck(
        state,
        later
      ).CheckHealthAsync(new());

      Assert.Equal(HealthStatus.Degraded, ready.Status);
      Assert.Contains(name, ready.Description);
      Assert.DoesNotContain(name, live.Description ?? "");
      Assert.DoesNotContain(name, BackgroundHeartbeat.Stalled(Now.AddDays(1)));
    }
    finally
    {
      BackgroundProgress.Forget(name);
    }
  }

  // Every background operation reports its progress, except the two that
  // are watched otherwise: the synchronization loop has a liveness
  // heartbeat, and the consistency audit's stall shows as stale coverage.
  [Fact]
  public void EveryBackgroundOperationReportsProgress()
  {
    var root = RepositoryFiles.Root();
    var missing = Directory
      .GetFiles(
        Path.Combine(root, "Server", "Application"),
        "*.cs",
        SearchOption.AllDirectories
      )
      .Where(file =>
        !file.Contains(
          $"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}"
        )
        && !file.Contains(
          $"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}"
        )
      )
      .Select(file => (file, source: File.ReadAllText(file)))
      .Where(x =>
        Regex.IsMatch(x.source, @"class \w+Operation\b[^{]*:\s*I\w+Operation\b")
      )
      .Where(x =>
        !x.source.Contains("BackgroundProgress.Expect(")
        && !x.source.Contains("BackgroundProgress.OnDemand(")
      )
      .Select(x => Path.GetFileName(x.file))
      .Order(StringComparer.Ordinal)
      .ToList();

    Assert.Equal(
      ["ConsistencyAuditOperation.cs", "FleetSynchronizationOperation.cs"],
      missing
    );
  }

  private static BackgroundProgress.Progress Of(
    string name,
    DateTimeOffset now
  ) => BackgroundProgress.Read(now).Single(x => x.Operation == name);

  private static string Name() => $"test-{Guid.NewGuid():N}";
}
