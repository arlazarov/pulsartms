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
    Assert.Equal(tone, TelemetryTone.Speed((decimal)speed, "On"));

  // The band is for a running engine, even at 0 mph; an engine that is off,
  // or a standing truck with no word of its engine, keeps the quiet icon
  // (the owner, September 27). Moving says the engine runs.
  [Theory]
  [InlineData(0, "Off", "is-stopped")]
  [InlineData(0, null, "is-stopped")]
  [InlineData(0, " idle ", "is-normal")]
  [InlineData(0, "Running", "is-normal")]
  [InlineData(40, null, "is-normal")]
  public void SpeedIsGreenOnlyWhileTheEngineRuns(
    int speed,
    string? engine,
    string tone
  ) => Assert.Equal(tone, TelemetryTone.Speed(speed, engine));

  // No report is not a stopped truck: it never reads as a normal speed.
  [Fact]
  public void AnUnknownSpeedIsNeverNormal() =>
    Assert.Equal("is-unknown", TelemetryTone.Speed(null, "On"));

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
