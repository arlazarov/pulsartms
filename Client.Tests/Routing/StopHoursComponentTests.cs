using Client.Shared.DriverStatus.ArrivalEstimate;
using Client.Shared.DriverStatus.StopHours;
using Client.Shared.DriverStatus;
using Bunit;
using Client.Models.DTO.Planning;
using Client.Shared;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;

namespace Client.Tests.Routing;

[Trait("Category", "Routing")]
[Trait("Kind", "Component")]
public sealed class StopHoursComponentTests
{
    private static readonly DateTimeOffset Start = new(2026, 9, 8, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void CompactSummaryPreservesTheCycleWarningWhileDetailedBalancesCanBeRenderedSeparately()
    {
        using var context = Context();
        var component = context.Render<StopHours>(p => p.Add(x => x.Estimate, Estimate(-80, -200))
            .Add(x => x.ShowDetails, false));
        Assert.Contains("Cycle short", component.Find(".stop-hours__road").TextContent);
        Assert.Empty(component.FindAll(".stop-hours__cycle"));

        component.Render(p => p.Add(x => x.ShowSummary, false).Add(x => x.ShowDetails, true));
        Assert.Empty(component.FindAll(".stop-hours__road"));
        Assert.Equal("−1h 20m", component.Find(".stop-hours__arrival-cycle strong").TextContent);
        Assert.Empty(component.FindAll(".stop-hours__departure-cycle"));
    }

    [Theory]
    [InlineData(2056)]
    [InlineData(0)]
    [InlineData(-60)]
    public void EqualArrivalAndDepartureCycleAreDisplayedOnlyOnce(int minutes)
    {
        using var context = Context();
        var component = Render(context, Estimate(minutes, minutes));
        Assert.Single(component.FindAll(".stop-hours__cycle .stop-hours__value"));
        Assert.Empty(component.FindAll(".stop-hours__departure-cycle"));
        Assert.Contains("Cycle remaining", component.Find(".stop-hours__arrival-cycle").TextContent);
        Assert.DoesNotContain("On arrival", component.Markup);
    }

    [Theory]
    [InlineData(2056, 2241)]
    [InlineData(-107, -38)]
    [InlineData(20, -100)]
    [InlineData(20, 0)]
    public void DepartureBalanceIsNeverDisplayedEvenWhenItDiffers(int arrival, int departure)
    {
        using var context = Context();
        var estimate = Estimate(arrival, departure);
        var component = Render(context, estimate);
        Assert.Equal(StopHoursDisplay.Signed(arrival), component.Find(".stop-hours__arrival-cycle strong").TextContent);
        Assert.Empty(component.FindAll(".stop-hours__departure-cycle"));
        Assert.DoesNotContain("After service", component.Markup);
        Assert.DoesNotContain("After delivery", component.Markup);
        Assert.Equal(departure, estimate.Hours!.CycleAfterStopMinutes);
    }

    [Fact]
    public void CycleHasItsOwnSectionSeparateFromRoadEta()
    {
        using var context = Context();
        var component = Render(context, Estimate(20, -100));
        var cycle = component.Find("section.stop-hours__cycle");
        Assert.Equal("Cycle remaining", cycle.QuerySelector("h3")!.TextContent);
        Assert.Equal("Cycle remaining", cycle.QuerySelector(".stop-hours__arrival-cycle h3")!.TextContent);
        Assert.Null(cycle.QuerySelector(".stop-hours__departure-cycle"));
        Assert.Null(cycle.QuerySelector(".stop-hours__road"));
        Assert.Single(component.FindAll(".stop-hours > .stop-hours__road"));
    }

    [Fact]
    public void NegativeCycleAtArrivalCannotProduceAGreenOnTimeClaim()
    {
        using var context = Context();
        var component = Render(context, Estimate(-80, -200));
        Assert.Equal("ETA", component.Find(".stop-hours__road .stop-hours__label").TextContent);
        Assert.DoesNotContain("Road ETA", component.Markup);
        Assert.Equal("−1h 20m", component.Find(".stop-hours__arrival-cycle strong").TextContent);
        Assert.Empty(component.FindAll(".stop-hours__departure-cycle"));
        Assert.Contains("Cycle short", component.Find(".stop-hours__status--danger").TextContent);
        Assert.Empty(component.FindAll(".stop-hours__status--success"));
    }

    [Fact]
    public void EarlierDrivingShortageRemainsVisibleAfterRecapRestoresCycle()
    {
        using var context = Context();
        var estimate = Estimate(200, 80);
        estimate = estimate with { Hours = estimate.Hours! with { FirstCycleShortageAt = Start } };
        var component = Render(context, estimate);
        Assert.Contains("+3h 20m", component.Find(".stop-hours__arrival-cycle").TextContent);
        Assert.Contains("Cycle short", component.Markup);
        Assert.Empty(component.FindAll(".stop-hours__status--success"));
    }

    [Fact]
    public void NegativeCycleAfterServiceDoesNotMakeTheArrivalInfeasible()
    {
        using var context = Context();
        var component = Render(context, Estimate(20, -100));
        Assert.Contains("On time", component.Find(".stop-hours__status--success").TextContent);
        Assert.Equal("+0h 20m", component.Find(".stop-hours__arrival-cycle strong").TextContent);
        Assert.Empty(component.FindAll(".stop-hours__departure-cycle"));
        Assert.DoesNotContain("Cycle short", component.Markup);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void UnknownCycleHidesUnverifiedHoursAndAlternativesButNotKnownRoadLateness(bool late)
    {
        using var context = Context();
        var estimate = Estimate(200, 80) with { LateMinutes = late ? 45 : 0 };
        estimate = estimate with { Hours = estimate.Hours! with { CycleVerified = false, Alternatives = [Alternative("recap", 0)] } };
        var component = Render(context, estimate);
        Assert.Contains("Cycle unknown", component.Find(".stop-hours__road").TextContent);
        Assert.Equal("—", component.Find(".stop-hours__arrival-cycle strong").TextContent);
        Assert.Empty(component.FindAll(".stop-hours__departure-cycle"));
        Assert.Empty(component.FindAll(".stop-hours__alternative, .stop-hours__status--success"));
        Assert.Equal(late ? 1 : 0, component.FindAll(".stop-hours__status--danger").Count);
        if (late) Assert.Contains("Late by 0h 45m", component.Markup);
    }

    [Fact]
    public void VerifiedHistoryWithoutArrivalCycleDoesNotClaimArrivalFeasibility()
    {
        using var context = Context();
        var estimate = Estimate(200, 80);
        estimate = estimate with { Hours = estimate.Hours! with { CycleAtArrivalMinutes = null } };
        var component = Render(context, estimate);
        Assert.Contains("Cycle unknown", component.Find(".stop-hours__road").TextContent);
        Assert.Equal("—", component.Find(".stop-hours__arrival-cycle strong").TextContent);
        Assert.Empty(component.FindAll(".stop-hours__status--success"));
    }

    [Fact]
    public void CycleShortAndRoadLatenessAreShownIndependently()
    {
        using var context = Context();
        var component = Render(context, Estimate(-80, -200) with { LateMinutes = 65 });
        Assert.Equal(2, component.FindAll(".stop-hours__road .stop-hours__status--danger").Count);
        Assert.Contains("Late by 1h 05m", component.Markup);
        Assert.Contains("Cycle short", component.Markup);
        var arrival = component.Find(".stop-hours__arrival");
        Assert.NotNull(arrival.QuerySelector("time"));
        Assert.Contains("Late by 1h 05m", arrival.TextContent);
        Assert.Null(arrival.QuerySelector(".stop-hours__cycle-status"));
        Assert.Equal("Cycle short", component.Find(".stop-hours__cycle-status").TextContent);
    }

    [Fact]
    public void RecapIsExplicitAndResetAlternativesAreNotDisplayed()
    {
        using var context = Context();
        var estimate = Estimate(-80, -200);
        estimate = estimate with { Hours = estimate.Hours! with { Alternatives =
            [Alternative("recap", 0), Alternative("restart", 0), Alternative("restart", 10), Alternative("unknown", 0)] } };
        var component = Render(context, estimate);
        Assert.Single(component.FindAll(".stop-hours__alternative"));
        Assert.Equal("With recap", component.Find("[data-kind=recap] .stop-hours__label").TextContent);
        Assert.Empty(component.FindAll("[data-kind=restart]"));
        Assert.Contains("On time with recap", component.Find("[data-kind=recap] .stop-hours__status--success").TextContent);
        Assert.DoesNotContain("On time if reset", component.Markup);
        Assert.Empty(component.FindAll(".stop-hours__alternative .stop-hours__status--conditional"));
        Assert.Empty(component.FindAll(".stop-hours__road .stop-hours__status--success"));
    }

    [Fact]
    public void ARecapAlternativeThatStillMissesTheAppointmentShowsItsOwnLateness()
    {
        using var context = Context();
        var estimate = Estimate(-80, -200);
        estimate = estimate with { Hours = estimate.Hours! with { Alternatives = [Alternative("recap", 130)] } };
        var component = Render(context, estimate);
        Assert.Contains("With recap", component.Find("[data-kind=recap]").TextContent);
        Assert.Contains("Late by 2h 10m", component.Find("[data-kind=recap] .stop-hours__status--danger").TextContent);
        Assert.DoesNotContain("On time with recap", component.Markup);
    }

    [Theory]
    [InlineData(null)]
    [InlineData(-1)]
    public void AlternativeWithoutKnownLatenessCannotPromiseAppointmentFeasibility(int? late)
    {
        using var context = Context();
        var estimate = Estimate(-80, -200);
        estimate = estimate with { Hours = estimate.Hours! with { Alternatives = [Alternative("recap", late)] } };
        Assert.Empty(Render(context, estimate).FindAll(".stop-hours__alternative"));
    }

    [Fact]
    public void MissingAppointmentCannotClaimOnTimeWithRecap()
    {
        using var context = Context();
        var estimate = Estimate(-80, -200) with { Appointment = null };
        estimate = estimate with { Hours = estimate.Hours! with { Alternatives = [Alternative("recap", 0)] } };
        Assert.Empty(Render(context, estimate).FindAll(".stop-hours__alternative"));
    }

    [Fact]
    public void DuplicateAlternativeKindsDoNotExpandTheCompactStopCard()
    {
        using var context = Context();
        var estimate = Estimate(-80, -200);
        estimate = estimate with { Hours = estimate.Hours! with { Alternatives = [Alternative("recap", 0), Alternative("recap", 20)] } };
        Assert.Single(Render(context, estimate).FindAll(".stop-hours__alternative"));
    }

    [Fact]
    public void PendingRetainsEveryForecastFieldStatusAndColorWithinTheDisplayGrace()
    {
        var clock = new FakeTimeProvider(Start);
        using var context = Context(clock);
        var estimate = Estimate(-80, -200) with { LateMinutes = 65 };
        estimate = estimate with { Hours = estimate.Hours! with { Alternatives = [Alternative("recap", 0), Alternative("restart", 0)] } };
        var stop = new PlanStop(estimate.StopId, "Delivery", "Warehouse", 1, new(40, -80));
        var complete = new DispatchEta(Start.UtcDateTime, Start.AddMinutes(2).UtcDateTime, [estimate], null, [])
        {
            CycleAtCalculation = new(400, Start.AddDays(3), 185, "America/New_York", true)
        };
        var component = context.Render<ArrivalEstimate>(p => p.Add(x => x.DispatchId, estimate.DispatchId)
            .Add(x => x.Stop, stop).Add(x => x.Eta, complete).Add(x => x.ShowRecap, true));
        var previousMarkup = component.Markup;
        clock.Advance(TimeSpan.FromMinutes(3));
        var pending = complete with { Stops = [], CycleAtCalculation = null, RouteUpdatePending = true };
        component.Render(p => p.Add(x => x.Eta, pending));
        Assert.Contains("Sep 8 · 01:00 PM", component.Find(".stop-hours__road").TextContent);
        Assert.Contains("−1h 20m", component.Find(".stop-hours__arrival-cycle").TextContent);
        Assert.Empty(component.FindAll(".stop-hours__departure-cycle"));
        Assert.Contains("+3h 05m", component.Find(".stop-hours__recap").TextContent);
        Assert.Single(component.FindAll(".stop-hours__alternative"));
        Assert.Equal(previousMarkup, component.Markup);
        Assert.Contains("Late by 1h 05m", component.Markup);
        Assert.Contains("Cycle short", component.Markup);
        Assert.DoesNotContain("Previous", component.Markup);
        Assert.DoesNotContain("Updating", component.Markup);
        Assert.Equal(2, component.FindAll(".stop-hours__road .stop-hours__status--danger").Count);
        Assert.Single(component.FindAll(".stop-hours__value--danger"));
        Assert.Single(component.FindAll(".stop-hours__status--success"));
        Assert.Empty(component.FindAll(".stop-hours__status--conditional"));

        clock.Advance(TimeSpan.FromMinutes(15));
        component.Render();
        Assert.Empty(component.FindAll(".stop-hours"));

        var refreshed = complete with { CalculatedAt = clock.GetUtcNow().UtcDateTime,
            ValidUntil = clock.GetUtcNow().AddMinutes(2).UtcDateTime,
            Stops = [estimate with { LateMinutes = 0, Hours = new(300, 180, 0, null, true, [], null) }] };
        component.Render(p => p.Add(x => x.Eta, refreshed));
        Assert.Equal("On time", component.Find(".stop-hours__status--success").TextContent);
        Assert.Equal("+5h 00m", component.Find(".stop-hours__arrival-cycle strong").TextContent);
        Assert.Empty(component.FindAll(".stop-hours__status--danger, .stop-hours__value--danger, .stop-hours__alternative"));
    }

    [Theory]
    [InlineData("stop")]
    [InlineData("load")]
    [InlineData("completed")]
    public void ChangedIdentityOrCompletionNeverInheritsHours(string changed)
    {
        using var context = Context();
        var estimate = Estimate(300, 180);
        var stop = new PlanStop(estimate.StopId, "Delivery", "Warehouse", 1, new(40, -80));
        var eta = new DispatchEta(Start.UtcDateTime, Start.AddMinutes(2).UtcDateTime, [estimate], null, []);
        var component = context.Render<ArrivalEstimate>(p => p.Add(x => x.DispatchId, estimate.DispatchId)
            .Add(x => x.Stop, stop).Add(x => x.Eta, eta));
        component.Render(p => p.Add(x => x.DispatchId, changed == "load" ? Guid.NewGuid() : estimate.DispatchId)
            .Add(x => x.Stop, changed == "stop" ? stop with { Id = Guid.NewGuid() } : stop)
            .Add(x => x.Completed, changed == "completed")
            .Add(x => x.Eta, eta with { Stops = [], RouteUpdatePending = true }));
        Assert.Empty(component.FindAll(".stop-hours"));
    }

    [Theory]
    [InlineData("2026-09-11T00:05:00+14:00", "Pacific/Kiritimati", "Sep 11")]
    [InlineData("2026-09-10T23:55:00-07:00", "America/Los_Angeles", "Sep 10")]
    public void RecapShowsOnlyTheHomeDateAndAmountWhilePreservingItsExactInstant(string value, string zone, string expected)
    {
        using var context = Context();
        var at = DateTimeOffset.Parse(value, System.Globalization.CultureInfo.InvariantCulture);
        var component = context.Render<StopHours>(p => p.Add(x => x.Estimate, Estimate(300, 180))
            .Add(x => x.ShowRecap, true).Add(x => x.Recap, new StopCycleForecast(300, at, 185, zone, true)));
        var recap = component.Find(".stop-hours__recap");
        Assert.Equal("Next recap", recap.QuerySelector(".stop-hours__label")!.TextContent);
        Assert.Equal(expected, recap.QuerySelector("time")!.TextContent);
        Assert.Equal(at, DateTimeOffset.Parse(recap.QuerySelector("time")!.GetAttribute("datetime")!,
            System.Globalization.CultureInfo.InvariantCulture));
        Assert.Contains("+3h 05m", recap.TextContent);
        Assert.DoesNotContain(":", recap.TextContent);
        Assert.DoesNotContain("home", recap.TextContent);
        Assert.DoesNotContain("UTC", recap.TextContent);
        Assert.Empty(recap.QuerySelectorAll("[title]"));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void UnknownOrExpiredNextRecapDoesNotShowAnUnusableDate(bool expired)
    {
        using var context = Context();
        var component = context.Render<StopHours>(p => p.Add(x => x.Estimate, Estimate(300, 180))
            .Add(x => x.ShowRecap, true)
            .Add(x => x.Recap, new StopCycleForecast(300, expired ? Start.AddHours(-1) : Start.AddDays(1), 185, "UTC", expired)));
        Assert.Empty(component.FindAll(".stop-hours__recap time"));
        Assert.DoesNotContain("+3h 05m", component.Find(".stop-hours__recap").TextContent);
    }

    [Fact]
    public void VerifiedHorizonWithoutNextRecapShowsKnownNoneInsteadOfUnknown()
    {
        using var context = Context();
        var component = context.Render<StopHours>(p => p.Add(x => x.Estimate, Estimate(300, 180))
            .Add(x => x.ShowRecap, true).Add(x => x.Recap, new StopCycleForecast(300, null, null, "UTC", true)));
        Assert.Equal("—", component.Find(".stop-hours__recap .stop-hours__value").TextContent.Trim());
        Assert.DoesNotContain("Unknown", component.Markup);
    }

    [Fact]
    public void ExpiredRecapShowsUnavailableWithoutAnOldDateOrCredit()
    {
        using var context = Context();
        var component = context.Render<StopHours>(p => p.Add(x => x.Estimate, Estimate(300, 180))
            .Add(x => x.ShowRecap, true)
            .Add(x => x.Recap, new StopCycleForecast(300, Start.AddMinutes(-1), 185, "UTC", true)));
        Assert.Equal("—", component.Find(".stop-hours__recap .stop-hours__value").TextContent.Trim());
        Assert.Empty(component.FindAll(".stop-hours__recap time"));
        Assert.DoesNotContain("+3h 05m", component.Find(".stop-hours__recap").TextContent);
        Assert.DoesNotContain("Unknown", component.Find(".stop-hours__recap").TextContent);
    }

    private static BunitContext Context(FakeTimeProvider? clock = null)
    {
        var context = new BunitContext();
        context.Services.AddSingleton<TimeProvider>(clock ?? new FakeTimeProvider(Start));
        return context;
    }

    private static IRenderedComponent<StopHours> Render(BunitContext context, StopEta estimate) =>
        context.Render<StopHours>(p => p.Add(x => x.Estimate, estimate));

    private static StopEta Estimate(int arrival, int departure) => new(Guid.NewGuid(), Start.AddHours(1), "UTC", Start.AddHours(2), 0, 60, 0)
    {
        DispatchId = Guid.NewGuid(), Hours = new(arrival, departure, 0, null, true, [], null)
    };

    private static StopHoursAlternative Alternative(string kind, int? late) =>
        new(kind, Start.AddHours(5), Start.AddHours(7), late, 300, Start, Start.AddHours(4));
}
