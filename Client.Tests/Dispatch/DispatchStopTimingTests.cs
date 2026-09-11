using Client.Shared.Dispatch.DispatchLoadStop;
using Client.Models.DTO.Dispatch;
using Client.Shared.Dispatch;
using Bunit;
using Client.Models.DTO.Planning;
using Client.Pages.Dispatch;
using Client.Tests.Support;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;

namespace Client.Tests.Dispatch;

[Trait("Category", "Dispatch")]
[Trait("Kind", "Component")]
public sealed class DispatchStopTimingTests
{
    private static readonly DateTimeOffset Start = new(2026, 9, 8, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void StopUsesItsOwnSavedRoadEtaAndHoursWithoutRequestsOrExplanationBlocks()
    {
        var requests = 0;
        using var context = new ClientComponentContext((_, _) =>
        {
            requests++;
            throw new InvalidOperationException("Stop timing must use the existing board forecast.");
        });
        context.Services.AddSingleton<TimeProvider>(new FakeTimeProvider(Start));
        var load = Load();
        load.Eta = load.Eta! with { Stops = load.Eta.Stops.Reverse().ToArray() };

        var component = Render(context, load);
        var timing = component.Find(".stop-hours");
        Assert.Equal("ETA", timing.QuerySelector(".stop-hours__road .stop-hours__label")!.TextContent);
        Assert.Contains("+14h 30m", component.Find(".stop-hours__arrival-cycle").TextContent);
        Assert.Empty(component.FindAll(".stop-hours__departure-cycle"));
        Assert.Contains("Late by 1h 05m", timing.TextContent);
        Assert.Empty(component.FindAll(".dispatch-load__timing, .dispatch-load__stop-cycle"));
        Assert.DoesNotContain("Plan to this stop", component.Markup);
        Assert.Equal(0, requests);
    }

    [Theory]
    [InlineData(null)]
    [InlineData(0)]
    public void OnTimeOrUnknownArrivalDoesNotClaimADelayReason(int? lateMinutes)
    {
        using var context = Context();
        var load = Load();
        ReplaceFinal(load, load.Eta!.Stops[^1] with { LateMinutes = lateMinutes });

        var component = Render(context, load);
        Assert.Empty(component.FindAll(".dispatch-load__timing, .stop-hours__status--danger"));
        Assert.Equal(lateMinutes == 0 ? 1 : 0, component.FindAll(".stop-hours__status--success").Count);
    }

    [Fact]
    public void StopAndDispatchIdentityMustBothMatch()
    {
        using var context = Context();
        var load = Load();
        var exact = load.Eta!.Stops[^1];
        load.Eta = load.Eta with
        {
            Stops = [exact with { DispatchId = Guid.NewGuid(), Hours = exact.Hours! with { CycleAtArrivalMinutes = 6000 } }, .. load.Eta.Stops]
        };
        var component = Render(context, load);
        Assert.Contains("+14h 30m", component.Find(".stop-hours__arrival-cycle").TextContent);
        Assert.DoesNotContain("100h", component.Markup);

        load.Eta = load.Eta with { Stops = [load.Eta.Stops[0], load.Eta.Stops[1]] };
        Update(component, load);
        Assert.Empty(component.FindAll(".stop-hours"));
    }

    [Fact]
    public void PendingPartialForecastRetainsRoadArrivalAndArrivalCycleUntilReplacementIsComplete()
    {
        var clock = new FakeTimeProvider(Start);
        using var context = Context(clock);
        var load = Load();
        var component = Render(context, load);
        var previousArrivalCycle = component.Find(".stop-hours__arrival-cycle").TextContent;
        Assert.Empty(component.FindAll(".stop-hours__departure-cycle"));
        var previousMarkup = component.Markup;
        clock.Advance(TimeSpan.FromMinutes(3));
        load.Eta = load.Eta! with
        {
            CalculatedAt = Start.AddMinutes(3).UtcDateTime,
            ValidUntil = Start.AddMinutes(5).UtcDateTime,
            Stops = [load.Eta.Stops[^1] with { Arrival = Start.AddDays(5), DrivingMinutes = 1, RestMinutes = 0,
                CycleAfterDeparture = null, Hours = null }],
            RouteUpdatePending = true
        };
        Update(component, load);

        Assert.Equal(previousArrivalCycle, component.Find(".stop-hours__arrival-cycle").TextContent);
        Assert.Empty(component.FindAll(".stop-hours__departure-cycle"));
        Assert.Contains("Sep 10 · 07:05 AM", component.Markup);
        Assert.DoesNotContain("+12h 30m", component.Markup);
        Assert.Equal(previousMarkup, component.Markup);
        Assert.DoesNotContain("Updating", component.Markup);
        Assert.Equal("Late by 1h 05m", component.Find(".stop-hours__status--danger").TextContent);
        Assert.DoesNotContain("Sep 13", component.Markup);

        load.Eta = load.Eta with
        {
            RouteUpdatePending = false,
            Stops = [load.Eta.Stops[0] with { DrivingMinutes = 600, RestMinutes = 1200,
                CycleAfterDeparture = new(500, null, null, null, false), Hours = new(620, 500, 0, null, true, [], null) }]
        };
        Update(component, load);
        Assert.Contains("+10h 20m", component.Find(".stop-hours__arrival-cycle").TextContent);
        Assert.Empty(component.FindAll(".stop-hours__departure-cycle"));
        Assert.DoesNotContain("+8h 20m", component.Markup);
        Assert.DoesNotContain("+12h 30m", component.Markup);
        Assert.DoesNotContain("Updating", component.Markup);
        Assert.DoesNotContain("Previously late", component.Markup);
        Assert.Equal("Late by 1h 05m", component.Find(".stop-hours__status--danger").TextContent);
    }

    [Fact]
    public void TransientMissingEstimateRetainsTheSamePlanWithinBoundedGrace()
    {
        var clock = new FakeTimeProvider(Start);
        using var context = Context(clock);
        var load = Load();
        var component = Render(context, load);
        var previousMarkup = component.Markup;
        load.Eta = null;
        clock.Advance(TimeSpan.FromMinutes(3));
        Update(component, load);

        Assert.Contains("+14h 30m", component.Find(".stop-hours__arrival-cycle").TextContent);
        Assert.Contains("Sep 10 · 07:05 AM", component.Markup);
        Assert.DoesNotContain("+12h 30m", component.Markup);
        Assert.Equal(previousMarkup, component.Markup);
        Assert.DoesNotContain("Updating", component.Markup);
        Assert.Equal("Late by 1h 05m", component.Find(".stop-hours__status--danger").TextContent);

        clock.Advance(TimeSpan.FromMinutes(15));
        Update(component, load);
        Assert.Empty(component.FindAll(".stop-hours"));
        Assert.DoesNotContain("+14h 30m", component.Markup);
    }

    [Fact]
    public void CompletedStopClearsItsRetainedHours()
    {
        using var context = Context();
        var load = Load();
        var component = Render(context, load);
        load.Stops[^1].DeliveredAt = Start.UtcDateTime;
        load.Eta = load.Eta! with { Stops = [], RouteUpdatePending = true };
        Update(component, load);
        Assert.Empty(component.FindAll(".stop-hours"));

        load.Stops[^1].DeliveredAt = null;
        Update(component, load);
        Assert.Empty(component.FindAll(".stop-hours"));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AnotherStopOrDispatchCannotInheritThePreviousPlan(bool changeDispatch)
    {
        using var context = Context();
        var load = Load();
        var component = Render(context, load);
        if (changeDispatch) load.Id = Guid.NewGuid();
        else load.Stops[^1].Id = Guid.NewGuid();
        load.Eta = load.Eta! with { Stops = [], RouteUpdatePending = true };
        Update(component, load);

        Assert.Empty(component.FindAll(".stop-hours"));
    }

    private static IRenderedComponent<DispatchLoadStop> Render(BunitContext context, DispatchResponse load) =>
        context.Render<DispatchLoadStop>(parameters => parameters.Add(stop => stop.DispatchId, load.Id)
            .Add(stop => stop.Stop, load.Stops[^1]).Add(stop => stop.Number, 2).Add(stop => stop.Eta, load.Eta));

    private static void Update(IRenderedComponent<DispatchLoadStop> component, DispatchResponse load) =>
        component.Render(parameters => parameters.Add(stop => stop.DispatchId, load.Id)
            .Add(stop => stop.Stop, load.Stops[^1]).Add(stop => stop.Number, 2).Add(stop => stop.Eta, load.Eta));

    private static BunitContext Context(FakeTimeProvider? clock = null)
    {
        var context = new BunitContext();
        context.Services.AddSingleton<TimeProvider>(clock ?? new FakeTimeProvider(Start));
        return context;
    }

    private static void ReplaceFinal(DispatchResponse load, StopEta final) =>
        load.Eta = load.Eta! with { Stops = [load.Eta.Stops[0], final] };

    private static DispatchResponse Load()
    {
        var load = new DispatchResponse
        {
            Id = Guid.NewGuid(), LoadNumber = 1000,
            Stops = Enumerable.Range(1, 2).Select(index => new DispatchStopResponse
            {
                Id = Guid.NewGuid(), Sequence = index, Job = index == 1 ? "Pickup" : "Delivery",
                Name = $"Facility {index}", City = "City", Province = "NY"
            }).ToList()
        };
        load.Eta = new(Start.UtcDateTime, Start.AddMinutes(2).UtcDateTime,
            load.Stops.Select((stop, index) => new StopEta(stop.Id,
                new(2026, 9, 10, 7, 5, 0, TimeSpan.FromHours(-4)), "America/New_York", null, 65,
                index == 0 ? 135 : 780, index == 0 ? 600 : 1800)
            {
                DispatchId = load.Id,
                CycleAfterDeparture = new(index == 0 ? 1000 : 750, null, null, null, false),
                Hours = new(index == 0 ? 1120 : 870, index == 0 ? 1000 : 750, 0, null, true, [], null)
            }).ToArray(), null, []);
        return load;
    }
}
