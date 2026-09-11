using Client.Shared.DriverStatus.ArrivalEstimate;
using Client.Models.DTO.Dispatch;
using Client.Shared.DriverStatus;
using System.Net;
using System.Net.Http.Json;
using Bunit;
using Client.Models.DTO.Planning;
using Client.Pages.Dispatch;
using Client.Services;
using Client.Shared;
using Client.Tests.Support;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;

namespace Client.Tests.Dispatch;

[Trait("Category", "Dispatch")]
[Trait("Kind", "Component")]
public sealed class DispatchPlanningRetentionTests
{
    private static readonly DateTimeOffset Start = new(2026, 9, 8, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void BoardHeaderGroupsTheDrivingStatusAndPumpReadingWithoutAnotherDataRequest()
    {
        var load = Load();
        var source = Result(load);
        var result = source with { State = source.State! with { FuelPercent = 28 } };
        var reads = 0;
        using var context = new ClientComponentContext((_, _) =>
        {
            reads++;
            return Task.FromResult(Json(result));
        });
        context.Services.AddSingleton<TimeProvider>(new FakeTimeProvider(Start));
        var component = context.Render<DispatchPlanning>(p => p.Add(x => x.TruckId, load.TruckId)
            .Add(x => x.Load, load).Add(x => x.Compact, true).Add(x => x.BoardHeader, true)
            .Add(x => x.MotionState, "moving").Add(x => x.MotionLabel, "Driving"));

        component.WaitForAssertion(() =>
        {
            var telemetry = component.Find(".dispatch-planning__telemetry");
            Assert.Contains("Driving", telemetry.QuerySelector(".dispatch-truck__status.is-moving")!.TextContent);
            Assert.Equal("Fuel 28%", telemetry.QuerySelector(".fuel-reading.is-low")!.TextContent.Trim());
            Assert.Equal("true", telemetry.QuerySelector(".fuel-reading__icon")!.GetAttribute("aria-hidden"));
            Assert.Equal(1, reads);
        });
    }

    [Fact]
    public void CompactHeaderShowsAuthoritativeTruckRecapBesideStatusEvenWhileRouteIsPending()
    {
        var load = Load();
        var result = Pending(Result(load));
        result = result with { State = result.State! with { Eta = result.State!.Eta! with {
            CycleAtCalculation = new(2000, Start.AddDays(5), 798, "UTC", true) } } };
        using var context = new ClientComponentContext((_, _) => Task.FromResult(Json(result)));
        context.Services.AddSingleton<TimeProvider>(new FakeTimeProvider(Start));
        var snapshot = new DriverCycleSnapshot(Start.UtcDateTime, Start.AddMinutes(2).UtcDateTime,
            new(1400, new(2026, 9, 11, 0, 0, 0, TimeSpan.FromHours(-4)), 185, "America/New_York", true));
        var component = context.Render<DispatchPlanning>(p => p.Add(x => x.TruckId, load.TruckId)
            .Add(x => x.Load, load).Add(x => x.Compact, true).Add(x => x.CurrentCycle, snapshot)
            .Add(x => x.Hos, new() { CurrentDutyStatus = "driving", UpdatedAt = Start.UtcDateTime }));

        component.WaitForAssertion(() =>
        {
            Assert.Equal("Sep 11", component.Find(".driver-next-recap time").TextContent);
            Assert.Contains("+3h 05m", component.Find(".driver-next-recap").TextContent);
            Assert.DoesNotContain("+13h 18m", component.Markup);
            Assert.Contains("Driving", component.Find(".dispatch-planning__driver .driver-duty").TextContent);
            Assert.Empty(component.FindAll(".driver-hours-panel .driver-duty, .dispatch-planning__details"));
            Assert.Equal(4, component.FindAll(".driver-hours__clock").Count);
        });

        component.Render(p => p.Add(x => x.CurrentCycle, null));
        Assert.Equal("—", component.Find(".driver-next-recap strong").TextContent.Trim());
        Assert.DoesNotContain("+13h 18m", component.Markup);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void MaintenanceMessageIsQuietOnlyForAValidRouteAndDoesNotHideProviderWarnings(bool inputsChanged)
    {
        var load = Load();
        var result = Result(load) with { Message = "Route update pending." };
        result.State!.Plan!.InputsChanged = inputsChanged;
        result.State.Plan.Route.Warnings = ["Route includes a restricted approach."];
        using var context = new ClientComponentContext((_, _) => Task.FromResult(Json(result)));
        context.Services.AddSingleton<TimeProvider>(new FakeTimeProvider(Start));
        var component = context.Render<DispatchPlanning>(p => p.Add(x => x.TruckId, load.TruckId).Add(x => x.Load, load));

        component.WaitForAssertion(() =>
        {
            Assert.Contains("Route includes a restricted approach.", component.Markup);
            if (inputsChanged) Assert.Contains("Route update pending.", component.Markup);
            else Assert.DoesNotContain("Route update pending.", component.Markup);
        });
    }

    [Fact]
    public void AddressReviewMessagesAreConciseWithoutChangingRouteFailureOrOtherWarnings()
    {
        const string reason = "The address correction needs confirmation of the street and building number; no city-center fallback was used.";
        var load = Load();
        var result = Result(load) with { Message = reason };
        result.State!.Plan!.InputsChanged = true;
        result.State.Plan.Route.Warnings = [reason, "Route includes a restricted approach."];
        using var context = new ClientComponentContext((_, _) => Task.FromResult(Json(result)));
        context.Services.AddSingleton<TimeProvider>(new FakeTimeProvider(Start));
        var component = context.Render<DispatchPlanning>(p => p.Add(x => x.TruckId, load.TruckId).Add(x => x.Load, load));

        component.WaitForAssertion(() =>
        {
            Assert.Contains("Check the stop address.", component.Markup);
            Assert.Contains("Route includes a restricted approach.", component.Markup);
            Assert.DoesNotContain("city-center fallback", component.Markup);
            Assert.True(result.State.Plan.InputsChanged);
            Assert.Equal(reason, result.Message);
        });
    }

    [Fact]
    public void TransientRefreshFailureIsQuietOnlyWhileThePreviousEstimateIsWithinItsGrace()
    {
        var load = Load();
        var clock = new FakeTimeProvider(Start);
        using var context = new ClientComponentContext((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)));
        context.Services.AddSingleton<TimeProvider>(clock);
        context.Services.GetRequiredService<PlanningDisplayCache>().Store($"api/fleet/trucks/{load.TruckId}/planning", Result(load));
        var component = context.Render<DispatchPlanning>(p => p.Add(x => x.TruckId, load.TruckId).Add(x => x.Load, load));
        component.WaitForAssertion(() =>
        {
            Assert.Contains("Sep 8 · 01:00 PM", component.Markup);
            Assert.Empty(component.FindAll(".dispatch-planning__hint"));
        });

        clock.Advance(TimeSpan.FromMinutes(18));
        component.Render();

        Assert.DoesNotContain("Sep 8 · 01:00 PM", component.Markup);
        Assert.Contains("temporarily unavailable", component.Find(".dispatch-planning__hint").TextContent);
    }

    [Theory]
    [InlineData("address")]
    [InlineData("appointment")]
    public void LatestLoadStopChangeCannotUseTheOlderCachedPlanForecast(string changedField)
    {
        var load = Load();
        using var context = new ClientComponentContext((_, _) => Task.FromResult(Json(Result(load))));
        context.Services.AddSingleton<TimeProvider>(new FakeTimeProvider(Start));
        var component = context.Render<DispatchPlanning>(p => p.Add(x => x.TruckId, load.TruckId).Add(x => x.Load, load));
        component.WaitForAssertion(() => Assert.Contains("Sep 8 · 01:00 PM", component.Markup));
        if (changedField == "address") load.Stops[0].Address = "200 Other St";
        else load.Stops[0].ScheduledTime = new(15, 0);

        component.Render(p => p.Add(x => x.Load, load));

        Assert.DoesNotContain("Sep 8 · 01:00 PM", component.Markup);
        Assert.Empty(component.FindAll(".arrival-estimate__ontime"));
    }

    [Fact]
    public void MissingPlanDuringRecalculationKeepsSummaryAndArrivalOnlyWithinTheExistingGrace()
    {
        var load = Load();
        var complete = Result(load);
        var pending = Pending(complete);
        var clock = new FakeTimeProvider(Start);
        var held = new TaskCompletionSource<HttpResponseMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        var reads = 0;
        using var context = new ClientComponentContext((_, _) => ++reads == 1 ? Task.FromResult(Json(pending)) : held.Task);
        context.Services.AddSingleton<TimeProvider>(clock);
        context.Services.GetRequiredService<PlanningDisplayCache>().Store($"api/fleet/trucks/{load.TruckId}/planning", complete);
        var component = context.Render<DispatchPlanning>(p => p.Add(x => x.TruckId, load.TruckId).Add(x => x.Load, load));
        component.WaitForAssertion(() =>
        {
            Assert.True(component.FindComponent<ArrivalEstimate>().Instance.Eta?.RouteUpdatePending);
            Assert.Contains("100 mi", component.Find(".dispatch-planning__metrics").TextContent);
            Assert.Contains("75 mi", component.Find(".dispatch-planning__metrics").TextContent);
            Assert.Equal("Fuel 50%", component.Find(".dispatch-planning__fuel").TextContent.Trim());
            Assert.Contains("Sep 8 · 01:00 PM", component.Markup);
            Assert.DoesNotContain("Updating", component.Markup);
            Assert.Single(component.FindAll(".arrival-estimate__ontime"));
        });
        clock.Advance(TimeSpan.FromMinutes(18));
        component.Render();
        Assert.Empty(component.FindAll(".dispatch-planning__metrics, .arrival-estimate"));
    }

    [Fact]
    public async Task ReadyReplacementAtomicallyReplacesTheRetainedSummaryAndEstimate()
    {
        var load = Load();
        var complete = Result(load);
        var clock = new FakeTimeProvider(Start);
        var held = new TaskCompletionSource<HttpResponseMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var reads = 0;
        using var context = new ClientComponentContext((_, _) =>
        {
            if (++reads == 1) return Task.FromResult(Json(Pending(complete)));
            started.TrySetResult();
            return held.Task;
        });
        context.Services.AddSingleton<TimeProvider>(clock);
        context.Services.GetRequiredService<PlanningDisplayCache>().Store($"api/fleet/trucks/{load.TruckId}/planning", complete);
        var component = context.Render<DispatchPlanning>(p => p.Add(x => x.TruckId, load.TruckId).Add(x => x.Load, load));
        component.WaitForAssertion(() => Assert.True(component.FindComponent<ArrivalEstimate>().Instance.Eta?.RouteUpdatePending));
        var previousMarkup = component.Find(".arrival-estimate").OuterHtml;
        await component.InvokeAsync(() => clock.Advance(TimeSpan.FromSeconds(10)));
        await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Contains("100 mi", component.Markup);
        Assert.Contains("Sep 8 · 01:00 PM", component.Markup);
        Assert.Equal(previousMarkup, component.Find(".arrival-estimate").OuterHtml);
        Assert.DoesNotContain("Updating", component.Markup);

        held.SetResult(Json(Result(load, 200, 14)));
        component.WaitForAssertion(() =>
        {
            Assert.Contains("200 mi", component.Find(".dispatch-planning__metrics").TextContent);
            Assert.Contains("Sep 8 · 02:00 PM", component.Markup);
            Assert.DoesNotContain("Sep 8 · 01:00 PM", component.Markup);
            Assert.DoesNotContain("Updating", component.Markup);
        });
    }

    [Fact]
    public void CompletingTheRetainedNextStopRemovesItsEstimateAndPendingSummaryImmediately()
    {
        var load = Load();
        var complete = Result(load);
        using var context = new ClientComponentContext((_, _) => Task.FromResult(Json(Pending(complete))));
        context.Services.AddSingleton<TimeProvider>(new FakeTimeProvider(Start));
        context.Services.GetRequiredService<PlanningDisplayCache>().Store($"api/fleet/trucks/{load.TruckId}/planning", complete);
        var component = context.Render<DispatchPlanning>(p => p.Add(x => x.TruckId, load.TruckId).Add(x => x.Load, load));
        component.WaitForAssertion(() => Assert.True(component.FindComponent<ArrivalEstimate>().Instance.Eta?.RouteUpdatePending));
        load.Stops[0].DeliveredAt = Start.UtcDateTime;
        component.Render(p => p.Add(x => x.Load, load));
        Assert.Empty(component.FindAll(".dispatch-planning__metrics, .arrival-estimate"));
    }

    [Fact]
    public void NewCurrentLoadOnTheSameTruckCannotShowOrRetainThePreviousLoad()
    {
        var first = Load();
        var second = Load(first.TruckId);
        var held = new TaskCompletionSource<HttpResponseMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        var reads = 0;
        using var context = new ClientComponentContext((_, _) => ++reads == 1 ? Task.FromResult(Json(Result(first))) : held.Task);
        context.Services.AddSingleton<TimeProvider>(new FakeTimeProvider(Start));
        var component = context.Render<DispatchPlanning>(p => p.Add(x => x.TruckId, first.TruckId).Add(x => x.Load, first));
        component.WaitForAssertion(() => Assert.Contains("Sep 8 · 01:00 PM", component.Markup));
        component.Render(p => p.Add(x => x.Load, second));
        Assert.Equal(2, reads);
        Assert.Empty(component.FindAll(".dispatch-planning__metrics, .arrival-estimate"));
        held.SetResult(Json(Result(second, 200, 14)));
        component.WaitForAssertion(() => Assert.Contains("Sep 8 · 02:00 PM", component.Markup));
        Assert.DoesNotContain("Sep 8 · 01:00 PM", component.Markup);
    }

    [Fact]
    public void NonPendingUnavailableResultDoesNotRetainTheOldSummary()
    {
        var load = Load();
        var complete = Result(load);
        var unavailable = Pending(complete);
        unavailable = unavailable with { State = unavailable.State! with { Eta = unavailable.State!.Eta! with { RouteUpdatePending = false } } };
        using var context = new ClientComponentContext((_, _) => Task.FromResult(Json(unavailable)));
        context.Services.AddSingleton<TimeProvider>(new FakeTimeProvider(Start));
        context.Services.GetRequiredService<PlanningDisplayCache>().Store($"api/fleet/trucks/{load.TruckId}/planning", complete);
        var component = context.Render<DispatchPlanning>(p => p.Add(x => x.TruckId, load.TruckId).Add(x => x.Load, load));
        component.WaitForAssertion(() => Assert.Empty(component.FindAll(".dispatch-planning__metrics, .arrival-estimate")));
    }

    private static DispatchResponse Load(Guid? truck = null) => new()
    {
        Id = Guid.NewGuid(), TruckId = truck ?? Guid.NewGuid(),
        Stops = [new() { Id = Guid.NewGuid(), Job = "Delivery", Sequence = 1, ScheduledDate = new(2026, 9, 8) }]
    };

    private static AutomaticPlanningResult Result(DispatchResponse load, double miles = 100, int hour = 13)
    {
        var stop = load.Stops[0];
        var plan = new RoutePlan { Id = Guid.NewGuid(), DispatchId = load.Id, TruckId = load.TruckId!.Value,
            Version = 1, OriginalPlannedMiles = miles,
            Stops = [new(stop.Id, "Delivery", "Warehouse", 1, new(40, -80))],
            Tracking = new() { NextStopId = stop.Id, NextStopLabel = "Delivery · Warehouse" } };
        var eta = new DispatchEta(Start.AddSeconds(hour).UtcDateTime, Start.AddMinutes(2).UtcDateTime,
            [new(stop.Id, Start.AddHours(hour - 12), "UTC", null, 0, 60, 0) { DispatchId = load.Id }], null, []);
        return new(load.TruckId.Value, load.Id, 1373,
            new(new(), plan, new(25, 75, 3600, 0, false, false, Start.UtcDateTime, null), 50, Start.UtcDateTime, true)
                { Eta = eta }, null);
    }

    private static AutomaticPlanningResult Pending(AutomaticPlanningResult result) => result with
    {
        State = result.State! with { Plan = null, Progress = null, FuelPercent = null,
            Eta = result.State!.Eta! with { CalculatedAt = Start.AddMinutes(1).UtcDateTime,
                RouteUpdatePending = true, Stops = [] } }
    };

    private static HttpResponseMessage Json(object value) => new(HttpStatusCode.OK)
        { Content = JsonContent.Create(new { success = true, response = value }) };
}
