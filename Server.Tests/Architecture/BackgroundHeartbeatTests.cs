using Application.Diagnostics;

namespace Server.Tests.Architecture;

// An instance died and nobody learned of it for seven and a half hours; the
// owner noticed a missing fuel stop, four layers from the cause. Liveness
// answered "alive" throughout, because it checked nothing.
//
// These fix the shape of the replacement. The dangerous failure is not
// missing a stall - it is calling a working instance stalled, because that
// restarts a healthy container in a loop.
[Trait("Category", "Architecture")]
[Trait("Kind", "Unit")]
public sealed class BackgroundHeartbeatTests
{
  // A heartbeat is written by the real clock, so a question about it has to
  // be asked of the real clock too. This was a date fixed on the day the
  // tests were written: "a day after it" stayed ahead of the beats for
  // exactly one day, and from noon the next the beat was in the future of
  // the question, nothing was stalled, and the test failed for good.
  private static DateTimeOffset Now => DateTimeOffset.UtcNow;

  [Fact]
  public void WorkNobodyRegisteredIsNeverCalledStalled()
  {
    var name = Name();

    Assert.DoesNotContain(name, BackgroundHeartbeat.Stalled(Now.AddDays(1)));
  }

  // An instance told to run a narrowed set of roles must not be restarted
  // for not doing work it was told not to do.
  [Fact]
  public void OnlyRegisteredWorkIsJudged()
  {
    var running = Name();
    var absent = Name();
    BackgroundHeartbeat.Expect(running, TimeSpan.FromMinutes(1));
    BackgroundHeartbeat.Beat(running);
    try
    {
      var stalled = BackgroundHeartbeat.Stalled(Now.AddDays(1));

      Assert.Contains(running, stalled);
      Assert.DoesNotContain(absent, stalled);
    }
    finally
    {
      BackgroundHeartbeat.Forget(running);
    }
  }

  [Fact]
  public void WorkThatJustReportedIsNotStalled()
  {
    var name = Name();
    BackgroundHeartbeat.Expect(name, TimeSpan.FromMinutes(1));
    try
    {
      BackgroundHeartbeat.Beat(name);

      Assert.DoesNotContain(
        name,
        BackgroundHeartbeat.Stalled(DateTimeOffset.UtcNow)
      );
    }
    finally
    {
      BackgroundHeartbeat.Forget(name);
    }
  }

  // A one-second loop must not restart the process over a ten-second pause,
  // so the tolerance has a floor.
  [Fact]
  public void AShortCycleStillGetsMinutesOfGrace()
  {
    var name = Name();
    BackgroundHeartbeat.Expect(name, TimeSpan.FromSeconds(1));
    try
    {
      BackgroundHeartbeat.Beat(name);

      Assert.DoesNotContain(
        name,
        BackgroundHeartbeat.Stalled(DateTimeOffset.UtcNow.AddMinutes(4))
      );
      Assert.Contains(
        name,
        BackgroundHeartbeat.Stalled(DateTimeOffset.UtcNow.AddMinutes(6))
      );
    }
    finally
    {
      BackgroundHeartbeat.Forget(name);
    }
  }

  // A failed cycle that was caught and retried is still a turning loop. Only
  // silence counts, or a provider outage would restart every instance.
  [Fact]
  public void ACycleThatFailedButReportedCountsAsAlive()
  {
    var name = Name();
    BackgroundHeartbeat.Expect(name, TimeSpan.FromMinutes(10));
    try
    {
      BackgroundHeartbeat.Beat(name);

      Assert.DoesNotContain(
        name,
        BackgroundHeartbeat.Stalled(DateTimeOffset.UtcNow.AddMinutes(30))
      );
    }
    finally
    {
      BackgroundHeartbeat.Forget(name);
    }
  }

  private static string Name() => "test-" + Guid.NewGuid().ToString("n");
}
