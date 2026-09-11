using Client.Models.DTO.Planning;
using Client.Pages.Dispatch;

namespace Client.Tests.Dispatch;

[Trait("Category", "Dispatch")]
[Trait("Kind", "Unit")]
public class DispatchRigStatusTests
{
    [Theory]
    [InlineData("idle", "idling")]
    [InlineData("idling", "idling")]
    [InlineData("On", "idling")]
    [InlineData("running", "idling")]
    [InlineData("off", "sleeping")]
    [InlineData("", "parked")]
    [InlineData(null, "parked")]
    public void SleepingDriverDoesNotOverrideRunningEngine(string? engine, string expected)
    {
        var now = DateTime.UtcNow;
        var hos = new DriverHosClocks { CurrentDutyStatus = "sleeperBerth", UpdatedAt = now };
        Assert.Equal(expected, DispatchRigStatus.Resolve(0, engine, hos, now));
        Assert.Equal("moving", DispatchRigStatus.Resolve(20, engine, hos, now));
    }

    [Fact]
    public void EngineOffAloneDoesNotMeanSleeper()
    {
        var now = DateTime.UtcNow;
        Assert.Equal("off", DispatchRigStatus.Resolve(0, "off", null, now));
        Assert.Equal("off", DispatchRigStatus.Resolve(0, "off",
            new() { CurrentDutyStatus = "offDuty", UpdatedAt = now }, now));
        Assert.Equal("off", DispatchRigStatus.Resolve(0, "off",
            new() { CurrentDutyStatus = "sleeperBerth", UpdatedAt = now.AddMinutes(-16) }, now));
    }
}
