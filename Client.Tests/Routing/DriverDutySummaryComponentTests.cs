using Client.Shared.DriverStatus.DriverDutySummary;
using Client.Shared.DriverStatus;
using Bunit;
using Client.Models.DTO.Planning;
using Client.Shared;

namespace Client.Tests.Routing;

[Trait("Category", "Routing")]
[Trait("Kind", "Component")]
public sealed class DriverDutySummaryComponentTests
{
    [Fact]
    public void MissingDutyStatusUsesOneNeutralPlaceholder()
    {
        using var context = new BunitContext();
        var component = context.Render<DriverDutySummary>();

        Assert.Equal("—", component.Find(".driver-duty__current strong").TextContent);
        Assert.DoesNotContain("waiting", component.Markup, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("unavailable", component.Markup, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(component.FindAll("small"));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void MissingRestHistoryAndStaleReadingsStayQuietWithoutInventingCountdowns(bool stale)
    {
        using var context = new BunitContext();
        var now = DateTimeOffset.UtcNow;
        var status = new DriverDutyStatus("sleeperBerth", now.AddHours(-6), null, stale ? now.AddMinutes(-4) : now);
        var component = context.Render<DriverDutySummary>(p => p.Add(x => x.Status, status));

        Assert.Contains("Sleeper Berth", component.Find(".driver-duty__current").TextContent);
        Assert.DoesNotContain("waiting", component.Markup, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("unavailable", component.Markup, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Last known", component.Markup, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("left to complete", component.Markup, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(component.FindAll(".driver-duty__cycle-reset"));
        if (stale) Assert.DoesNotContain("for ", component.Find(".driver-duty__current").TextContent);
    }

    [Theory]
    [InlineData(35, "35 min")]
    [InlineData(95, "1h 35min")]
    public void CurrentStatusExplicitlyLabelsElapsedTime(int minutes, string duration)
    {
        using var context = new BunitContext();
        var now = DateTimeOffset.UtcNow;
        var status = new DriverDutyStatus("driving", now.AddMinutes(-minutes), null, now);
        var component = context.Render<DriverDutySummary>(p => p.Add(x => x.Status, status));
        var current = component.Find(".driver-duty__current").TextContent;
        Assert.Contains("HOS status:", current);
        Assert.Contains($"Driving for {duration}", current);
        component.Render(p => p.Add(x => x.CurrentStatus, "onDuty"));
        Assert.DoesNotContain("for ", component.Find(".driver-duty__current").TextContent);
        component.Render(p => p.Add(x => x.CurrentStatus, "driving")
            .Add(x => x.Status, status with { ObservedAt = now.AddMinutes(-4) }));
        Assert.DoesNotContain("for ", component.Find(".driver-duty__current").TextContent);
    }

    [Theory]
    [InlineData("US", 34, 1672, "27h 52m")]
    [InlineData("CA", 36, 1792, "29h 52m")]
    [InlineData("CA", 72, 3952, "65h 52m")]
    public void DisplaysServerResetCountdownBesideDailyRest(string country, int hours, int remaining, string text)
    {
        using var context = new BunitContext();
        var now = DateTimeOffset.UtcNow;
        var status = new DriverDutyStatus("sleeperBerth", now.AddMinutes(-368), now.AddMinutes(-368), now)
        { CycleResetHours = hours, CycleResetCountry = country, CycleResetRemainingMinutes = remaining };
        var component = context.Render<DriverDutySummary>(p => p.Add(x => x.Status, status));
        Assert.Contains("3h 52m left to complete 10h rest", component.Markup);
        Assert.Equal($"{text} left to complete {hours}h reset · {country}", component.Find(".driver-duty__cycle-reset").TextContent);
    }

    [Fact]
    public void MissingStaleOrMismatchedStatusHidesResetCountdown()
    {
        using var context = new BunitContext();
        var now = DateTimeOffset.UtcNow;
        var status = new DriverDutyStatus("sleeperBerth", now.AddHours(-6), now.AddHours(-6), now);
        var component = context.Render<DriverDutySummary>(p => p.Add(x => x.Status, status));
        Assert.Empty(component.FindAll(".driver-duty__cycle-reset"));
        status = status with { CycleResetHours = 34, CycleResetCountry = "US", CycleResetRemainingMinutes = 1680 };
        component.Render(p => p.Add(x => x.Status, status).Add(x => x.CurrentStatus, "driving"));
        Assert.Empty(component.FindAll(".driver-duty__cycle-reset"));
        component.Render(p => p.Add(x => x.CurrentStatus, "sleeperBerth").Add(x => x.Status, status with { ObservedAt = now.AddMinutes(-4) }));
        Assert.Empty(component.FindAll(".driver-duty__cycle-reset"));
    }

    [Fact]
    public void CompletedResetShowsReachedInsteadOfNegativeCountdown()
    {
        using var context = new BunitContext();
        var now = DateTimeOffset.UtcNow;
        var status = new DriverDutyStatus("offDuty", now.AddHours(-36), now.AddHours(-36), now)
        { CycleResetHours = 34, CycleResetCountry = "US", CycleResetRemainingMinutes = 0 };
        var component = context.Render<DriverDutySummary>(p => p.Add(x => x.Status, status));
        Assert.Equal("34h reset reached · US", component.Find(".driver-duty__cycle-reset").TextContent);
    }
}
