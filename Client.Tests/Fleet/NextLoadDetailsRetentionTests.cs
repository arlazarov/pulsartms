using Client.Models.DTO.Dispatch;
using Bunit;
using Client.Models.DTO.Planning;
using Client.Pages.Dispatch;
using Client.Pages.FleetMap;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;

namespace Client.Tests.Fleet;

[Trait("Category", "Fleet")]
[Trait("Kind", "Component")]
public sealed class NextLoadDetailsRetentionTests
{
    [Fact]
    public void FuelArrivalIsAlwaysVisibleAndMustMatchTheExactLoadAndStop()
    {
        using var context = new BunitContext();
        var clock = new FakeTimeProvider();
        context.Services.AddSingleton<TimeProvider>(clock);
        var (route, stop, _) = Inputs(clock);
        var arrival = new FuelStopArrival(route.Id, stop.Id, 82, 32.8);
        var component = context.Render<NextLoadDetailsCard>(p => p.Add(x => x.Route, route)
            .Add(x => x.Stop, stop).Add(x => x.FuelArrival, arrival));
        Assert.Single(component.FindAll(".fleet-route-popup__information .fleet-route-popup__fuel"));
        Assert.Empty(component.FindAll(".fleet-route-popup__location .fleet-route-popup__fuel"));
        Assert.Equal("33%", component.Find(".fleet-fuel-visit__percent").TextContent);
        Assert.Equal("82 US gal", component.Find(".fleet-fuel-visit__quantity").TextContent);
        Assert.Equal("33 100", component.Find(".driver-hours__arc").GetAttribute("stroke-dasharray"));
        component.Render(p => p.Add(x => x.FuelArrival, arrival with { StopId = Guid.NewGuid() }));
        Assert.Equal("—", component.Find(".fleet-fuel-visit__percent").TextContent);
        component.Render(p => p.Add(x => x.FuelArrival, arrival with { DispatchId = Guid.NewGuid() }));
        Assert.Equal("—", component.Find(".fleet-fuel-visit__percent").TextContent);
        component.Render(p => p.Add(x => x.FuelArrival, null));
        Assert.Equal("—", component.Find(".fleet-fuel-visit__percent").TextContent);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void UnavailableFutureStopShowsOneCompactPlaceholderWithoutTheTechnicalReason(bool pending)
    {
        using var context = new BunitContext();
        var clock = new FakeTimeProvider();
        context.Services.AddSingleton<TimeProvider>(clock);
        var (route, stop, eta) = Inputs(clock);
        const string reason = "ETA unavailable: waiting for the saved connection from the preceding load.";
        eta = eta with { Stops = [], UnavailableReason = reason, RouteUpdatePending = pending };
        var component = context.Render<NextLoadDetailsCard>(p => p.Add(x => x.Route, route)
            .Add(x => x.Stop, stop).Add(x => x.Eta, eta));

        Assert.Equal("ETA —", Assert.Single(component.FindAll(".fleet-route-popup__eta")).TextContent);
        Assert.Empty(component.FindAll(".arrival-estimate, .stop-hours__cycle"));
        Assert.DoesNotContain(reason, component.Markup);
    }

    [Fact]
    public void FutureSelectedStopKeepsRoadEtaHoursAndNearestRecapUntilTheCompleteReplacement()
    {
        using var context = new BunitContext();
        var clock = new FakeTimeProvider(new DateTimeOffset(2026, 9, 8, 12, 0, 0, TimeSpan.Zero));
        context.Services.AddSingleton<TimeProvider>(clock);
        var (route, stop, eta) = Inputs(clock);
        eta = eta with
        {
            Stops = [eta.Stops[0] with { Appointment = clock.GetUtcNow().AddHours(7), Hours = new(-60, -180, 60, clock.GetUtcNow(), true,
                [new("recap", clock.GetUtcNow().AddHours(6), clock.GetUtcNow().AddHours(8), 0, 300, null, null)], null) }],
            CycleAtCalculation = new(400, clock.GetUtcNow().AddDays(3), 185, "UTC", true)
        };
        var component = context.Render<NextLoadDetailsCard>(p => p.Add(x => x.Route, route)
            .Add(x => x.Stop, stop).Add(x => x.Eta, eta));
        var previousMarkup = component.Find(".arrival-estimate").OuterHtml;
        clock.Advance(TimeSpan.FromMinutes(3));
        component.Render(p => p.Add(x => x.Eta, eta with { Stops = [], RouteUpdatePending = true, CycleAtCalculation = null }));
        Assert.Equal("ETA", component.Find(".stop-hours__road .stop-hours__label").TextContent);
        Assert.Contains("−1h 00m", component.Find(".stop-hours__arrival-cycle").TextContent);
        Assert.Empty(component.FindAll(".stop-hours__departure-cycle"));
        Assert.Contains("+3h 05m", component.Find(".stop-hours__recap").TextContent);
        Assert.Equal(previousMarkup, component.Find(".arrival-estimate").OuterHtml);
        Assert.Contains("Cycle short", component.Find(".stop-hours__status--danger").TextContent);
        Assert.DoesNotContain("Previous", component.Markup);
        Assert.DoesNotContain("Updating", component.Markup);
        Assert.Single(component.FindAll(".stop-hours__alternative"));
        Assert.Single(component.FindAll(".stop-hours__status--success"));
        Assert.Empty(component.FindAll(".fleet-route-popup__eta"));

        var refreshed = eta with { CalculatedAt = clock.GetUtcNow().UtcDateTime,
            ValidUntil = clock.GetUtcNow().AddMinutes(2).UtcDateTime,
            Stops = [eta.Stops[0] with { Hours = new(300, 180, 0, null, true, [], null) }] };
        component.Render(p => p.Add(x => x.Eta, refreshed));
        Assert.Equal("+5h 00m", component.Find(".stop-hours__arrival-cycle strong").TextContent);
        Assert.Equal("On time", component.Find(".stop-hours__status--success").TextContent);
        Assert.Empty(component.FindAll(".stop-hours__status--danger, .stop-hours__alternative"));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void MissingOrPendingEtaDoesNotAddADashBesideTheRetainedEstimate(bool pending)
    {
        using var context = new BunitContext();
        var clock = new FakeTimeProvider(new DateTimeOffset(2026, 9, 8, 12, 0, 0, TimeSpan.Zero));
        context.Services.AddSingleton<TimeProvider>(clock);
        var (route, stop, eta) = Inputs(clock);
        var component = context.Render<NextLoadDetailsCard>(p => p.Add(x => x.Route, route)
            .Add(x => x.Stop, stop).Add(x => x.Eta, eta));
        Assert.Single(component.FindAll(".arrival-estimate"));
        Assert.Empty(component.FindAll(".fleet-route-popup__eta"));
        var previousMarkup = component.Find(".arrival-estimate").OuterHtml;

        clock.Advance(TimeSpan.FromMinutes(3));
        var incoming = pending ? eta with { Stops = [], RouteUpdatePending = true } : null;
        component.Render(p => p.Add(x => x.Eta, incoming));
        Assert.Contains("Sep 8 · 01:00 PM", component.Markup);
        Assert.Equal(previousMarkup, component.Find(".arrival-estimate").OuterHtml);
        Assert.DoesNotContain("Updating", component.Markup);
        Assert.Empty(component.FindAll(".fleet-route-popup__eta"));

        clock.Advance(TimeSpan.FromMinutes(15));
        component.Render();
        Assert.Empty(component.FindAll(".arrival-estimate"));
        Assert.Equal("ETA —", component.Find(".fleet-route-popup__eta").TextContent);
    }

    [Theory]
    [InlineData("stop")]
    [InlineData("load")]
    [InlineData("completed")]
    public void DifferentOrCompletedStopsCannotRetainThePriorEstimate(string changed)
    {
        using var context = new BunitContext();
        var clock = new FakeTimeProvider(new DateTimeOffset(2026, 9, 8, 12, 0, 0, TimeSpan.Zero));
        context.Services.AddSingleton<TimeProvider>(clock);
        var (route, stop, eta) = Inputs(clock);
        var component = context.Render<NextLoadDetailsCard>(p => p.Add(x => x.Route, route)
            .Add(x => x.Stop, stop).Add(x => x.Eta, eta));
        DispatchResponse? details = changed == "completed" ? new()
        {
            Id = route.Id,
            Stops = [new() { Id = stop.Id, DeliveredAt = clock.GetUtcNow().UtcDateTime }]
        } : null;
        component.Render(p => p.Add(x => x.Route, changed == "load" ? route with { Id = Guid.NewGuid() } : route)
            .Add(x => x.Stop, changed == "stop" ? stop with { Id = Guid.NewGuid() } : stop)
            .Add(x => x.Details, details).Add(x => x.Eta, (DispatchEta?)null));
        Assert.Empty(component.FindAll(".arrival-estimate"));
        Assert.Equal("ETA —", component.Find(".fleet-route-popup__eta").TextContent);
        Assert.DoesNotContain("Sep 8 · 01:00 PM", component.Markup);
    }

    private static (NextLoadRoute, PlanStop, DispatchEta) Inputs(FakeTimeProvider clock)
    {
        var id = Guid.NewGuid();
        var stop = new PlanStop(Guid.NewGuid(), "Warehouse", "123 Main Street", 1, new(40, -80));
        var route = new NextLoadRoute(id, 1373, "ready", [], [new(40, -80, "Delivery") { Id = stop.Id }]);
        var now = clock.GetUtcNow().UtcDateTime;
        var eta = new DispatchEta(now, now.AddMinutes(2),
            [new(stop.Id, now.AddHours(1), "UTC", null, 0, 60, 0) { DispatchId = id }], null, []);
        return (route, stop, eta);
    }
}
