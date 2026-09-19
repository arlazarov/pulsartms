using Client.Shared;
using Client.Shared.Trucks;

namespace Client.Tests.Fleet;

[Trait("Category", "Fleet")]
[Trait("Kind", "Unit")]
public sealed class TelemetryToneTests
{
  [Theory]
  [InlineData(0, "is-normal")]
  [InlineData(65, "is-normal")]
  [InlineData(65.1, "is-low")]
  [InlineData(70, "is-low")]
  [InlineData(70.1, "is-critical")]
  public void SpeedUsesInclusiveLimits(double speed, string tone) =>
    Assert.Equal(tone, TelemetryTone.Speed((decimal)speed));

  [Theory]
  [InlineData(65, "On", "is-normal")]
  [InlineData(1, "Idle", "is-normal")]
  [InlineData(0, "Idle", "is-low")]
  [InlineData(0, " idling ", "is-low")]
  [InlineData(0, "On", "is-low")]
  [InlineData(0, "running", "is-low")]
  [InlineData(0, "Off", "is-unknown")]
  [InlineData(0, null, "is-unknown")]
  public void EngineUsesMotionThenIdleAndKeepsOffNeutral(
    int speed,
    string? state,
    string tone
  ) => Assert.Equal(tone, TelemetryTone.Engine(speed, state));
}
