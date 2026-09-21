using Domain.Models.Fleet;
using Domain.Rules.Eta;

namespace Server.Tests.Eta;

[Trait("Category", "Eta")]
[Trait("Kind", "Unit")]
public sealed class StopServicePolicyTests
{
  [Theory]
  [InlineData("Collect truck", 0)]
  [InlineData("Collect trailer", 0)]
  [InlineData("Drop trailer", 0)]
  [InlineData("Waypoint", 0)]
  [InlineData("Pick Up", 120)]
  [InlineData("Drop Off", 90)]
  public void EquipmentActionsDoNotInheritCargoService(
    string job,
    int expected
  ) => Assert.Equal(expected, StopServicePolicy.Minutes(job, 120, 90));

  [Fact]
  public void TruckCollectionWaitDoesNotInventAFullRest()
  {
    var now = DateTimeOffset.UtcNow;
    var clock = new HosTravelClock(
      now,
      new DriverHosClocks
      {
        BreakMs = 8 * 3600000L,
        DriveMs = 11 * 3600000L,
        ShiftMs = 14 * 3600000L,
        CycleMs = 70 * 3600000L,
      },
      "US"
    );
    StopServicePolicy.WaitUntil(clock, now.AddHours(10), "Collect truck");
    Assert.Equal(now.AddHours(10), clock.Now);
    Assert.Equal(0, clock.RestHours);
    Assert.Equal(0, clock.PlannedOffDutyWaitHours);
    Assert.Equal(60 * 60, clock.SnapshotCycle().RemainingMinutes);
  }
}
