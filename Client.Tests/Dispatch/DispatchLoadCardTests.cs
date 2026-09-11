using Client.Shared.Dispatch.DispatchLoadDialog;
using Client.Models.DTO.Dispatch;
using Client.Shared.Dispatch;
using Bunit;
using Client.Models.DTO.Planning;
using Client.Pages.Dispatch;
using Client.Shared;
using Client.Tests.Support;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.JSInterop;

namespace Client.Tests.Dispatch;

[Trait("Category", "Dispatch")]
[Trait("Kind", "Component")]
public sealed class DispatchLoadCardTests
{
    private static readonly DateTimeOffset Start = new(2026, 9, 8, 12, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData(true, false, true, true)]
    [InlineData(false, false, false, true)]
    [InlineData(true, true, false, false)]
    public void FooterShowsOnlyEligibleLivePlanningValues(bool current, bool completed, bool remainingVisible, bool fuelVisible)
    {
        using var context = new BunitContext();
        context.Services.AddSingleton<TimeProvider>(new FakeTimeProvider(Start));
        var load = Load();
        if (completed) load.Status = "completed";
        var component = context.Render<DispatchLoadCard>(p => p.Add(card => card.Load, load)
            .Add(card => card.Current, current).Add(card => card.RemainingMiles, 302)
            .Add(card => card.FuelStopCount, 2));

        Assert.Equal(remainingVisible ? 1 : 0, component.FindAll(".dispatch-load__remaining").Count);
        Assert.Equal(fuelVisible ? 1 : 0, component.FindAll(".dispatch-load__fuel-count").Count);
        if (remainingVisible) Assert.Equal("302 mi remaining", component.Find(".dispatch-load__remaining").TextContent);
        if (fuelVisible) Assert.Contains("Fuel stops 2", component.Find(".dispatch-load__fuel-count").TextContent);
        Assert.DoesNotContain("1,000 mi", component.Find(".dispatch-load__footer").TextContent);
    }

    [Fact]
    public void UnknownFooterPlanningIsNotShownAsZeroOrReplacedByTheTotalLoadDistance()
    {
        using var context = new BunitContext();
        context.Services.AddSingleton<TimeProvider>(new FakeTimeProvider(Start));
        var component = context.Render<DispatchLoadCard>(p => p.Add(card => card.Load, Load()).Add(card => card.Current, true));
        Assert.Empty(component.FindAll(".dispatch-load__remaining, .dispatch-load__fuel-count"));
        Assert.Contains("3 stops", component.Find(".dispatch-load__footer").TextContent);
        Assert.DoesNotContain("1,000 mi", component.Find(".dispatch-load__footer").TextContent);

        component.Render(p => p.Add(card => card.RemainingMiles, double.NaN).Add(card => card.FuelStopCount, 0));
        Assert.Empty(component.FindAll(".dispatch-load__remaining"));
        Assert.Contains("Fuel stops 0", component.Find(".dispatch-load__fuel-count").TextContent);
    }

    [Fact]
    public async Task CompactStopsKeepPrimaryInformationVisibleAndOpenSupplementalDetailsWithoutRequests()
    {
        var requests = 0;
        await using var context = new ClientComponentContext((_, _) =>
        {
            requests++;
            throw new InvalidOperationException("Load details must use the existing board data.");
        });
        context.Services.AddSingleton<TimeProvider>(new FakeTimeProvider(Start));
        var load = Load();
        foreach (var stop in load.Stops)
        {
            stop.Address = $"{stop.Sequence} Warehouse Road";
            stop.StopNo = $"REF-{stop.Sequence}";
            stop.Notes = "Shipper appointment confirmation number: PU123. Receiver appointment confirmation number: DEL456.";
        }
        load.Stops[0].PickedUpAt = Start.AddHours(-1).UtcDateTime;
        var component = context.Render<DispatchLoadCard>(p => p.Add(card => card.Load, load).Add(card => card.Current, true));

        foreach (var stop in component.FindAll(".dispatch-load__stop"))
        {
            Assert.Null(stop.QuerySelector("details, .dispatch-load__stop-detail-content, .dispatch-load__reference-number, .dispatch-load__appointment-reference"));
            Assert.NotNull(stop.QuerySelector(":scope > .dispatch-load__location"));
            Assert.NotNull(stop.QuerySelector(":scope > .dispatch-load__street"));
            Assert.NotNull(stop.QuerySelector(".dispatch-load__stop-times .arrival-estimate__appointment"));
        }
        Assert.Single(component.FindAll(".dispatch-load__stop--completed"));
        Assert.Equal(2, component.FindAll(".dispatch-load__stop-times .arrival-estimate > span:not(.arrival-estimate__appointment) > strong").Count);
        Assert.Single(component.FindAll(".dispatch-load__stop-times .arrival-estimate__late"));
        Assert.Empty(component.FindAll(".arrival-estimate__timezone"));
        await OpenDetailsAsync(context, component);
        Assert.Same(load, component.FindComponent<DispatchLoadDialog>().Instance.Load);
        Assert.Equal("Current", component.Find(".dispatch-load-dialog .dispatch-paper__phase").TextContent);
        foreach (var stop in component.FindAll(".dispatch-load-dialog .dispatch-load__stop"))
        {
            Assert.NotNull(stop.QuerySelector(".dispatch-load__facility"));
            Assert.NotNull(stop.QuerySelector(".dispatch-load__street"));
            Assert.NotNull(stop.QuerySelector(".dispatch-load__reference-number"));
            Assert.NotNull(stop.QuerySelector(".dispatch-load__appointment-reference"));
            Assert.False(stop.QuerySelector("details")!.HasAttribute("open"));
            Assert.NotNull(stop.QuerySelector(":scope > .dispatch-load__street"));
            Assert.NotNull(stop.QuerySelector("details .dispatch-load__reference-number"));
        }
        Assert.Equal(2, component.FindAll(".dispatch-load-dialog .arrival-estimate__timezone").Count);
        Assert.Single(component.FindAll(".dispatch-load-dialog .dispatch-load__stop--completed"));
        Assert.Empty(component.FindAll(".dispatch-load-dialog .dispatch-load__stop-detail-content .arrival-estimate__late, .dispatch-load-dialog .dispatch-load__stop-detail-content .arrival-estimate__ontime"));
        Assert.Equal(0, requests);
    }

    [Theory]
    [InlineData(true, 0, "Current", false)]
    [InlineData(false, 1, "Next", true)]
    [InlineData(false, 2, "Upcoming", false)]
    public void OnlyTheNextLoadGetsThePurplePresentationState(bool current, int order, string phase, bool next)
    {
        using var context = new BunitContext();
        context.Services.AddSingleton<TimeProvider>(new FakeTimeProvider(Start));
        var component = context.Render<DispatchLoadCard>(p => p.Add(card => card.Load, Load())
            .Add(card => card.Current, current).Add(card => card.Order, order));
        Assert.Equal(phase, component.Find(".dispatch-load__phase").TextContent);
        Assert.Equal(next, component.Find(".dispatch-load").ClassList.Contains("dispatch-load--next"));
    }

    [Fact]
    public async Task LaneCardKeepsEveryStopButShowsMileageAndPricesOnlyInDetails()
    {
        using var context = new BunitContext();
        context.Services.AddSingleton<TimeProvider>(new FakeTimeProvider(Start));
        var load = Load();
        load.Stops[0].PickedUpAt = Start.AddHours(-1).UtcDateTime;
        foreach (var stop in load.Stops) stop.Address = $"{stop.Sequence} Warehouse Road";
        var component = context.Render<DispatchLoadCard>(p => p.Add(card => card.Load, load).Add(card => card.Current, true));

        Assert.Equal(3, component.FindAll(".dispatch-load__route > .dispatch-load__route-stop").Count);
        Assert.Equal(new[] { "1", "2", "3" }, component.FindAll(".dispatch-load__stop-number").Select(item => item.TextContent));
        Assert.Equal(new[] { "1 Warehouse Road", "2 Warehouse Road", "3 Warehouse Road" },
            component.FindAll(".dispatch-load__street").Select(item => item.TextContent));
        Assert.Single(component.FindAll(".dispatch-load__stop--completed"));
        Assert.Empty(component.FindAll(".dispatch-load__summary, .dispatch-load__metrics, .dispatch-paper__financials"));
        Assert.Equal("true", component.Find(".dispatch-load__connector").GetAttribute("aria-hidden"));
        Assert.Equal("button", component.Find(".dispatch-load__footer .dispatch-load__details").GetAttribute("type"));
        Assert.StartsWith("Details for load ", component.Find(".dispatch-load__footer .dispatch-load__details").GetAttribute("aria-label"));
        Assert.Contains("3 stops", component.Find(".dispatch-load__footer").TextContent);
        Assert.DoesNotContain("New load", component.Markup);
        await OpenDetailsAsync(context, component);
        var financials = component.Find(".dispatch-paper__financials").TextContent;
        foreach (var value in new[] { "900 mi", "100 mi", "1,000 mi", "2,500.00 CAD", "2.78 CAD", "2.50 CAD" })
            Assert.Contains(value, financials);
    }

    [Fact]
    public void LaneCardOnlyOmitsAssignmentsAlreadyShownInItsTruckHeader()
    {
        using var context = new BunitContext();
        context.Services.AddSingleton<TimeProvider>(new FakeTimeProvider(Start));
        var load = Load();
        load.DriverName = "Same Driver";
        load.TrailerNumber = "GG1030";
        var component = context.Render<DispatchLoadCard>(p => p.Add(card => card.Load, load)
            .Add(card => card.HeaderDriverName, " same driver ").Add(card => card.HeaderTrailerNumber, "gg1030"));
        Assert.Empty(component.FindAll(".dispatch-load__assignment"));

        load.DriverName = "Next Driver";
        component.Render(p => p.Add(card => card.Load, load));
        Assert.Equal("Driver Next Driver", Assert.Single(component.FindAll(".dispatch-load__assignment")).TextContent);
        load.TrailerNumber = "NEXT-TRAILER";
        component.Render(p => p.Add(card => card.Load, load));
        Assert.Equal(2, component.FindAll(".dispatch-load__assignment").Count);
        Assert.Contains("Trailer NEXT-TRAILER", component.Find(".dispatch-load__meta").TextContent);
    }

    [Fact]
    public void StreetIsNotRepeatedWhenItIsAlreadyTheStopLocationFallback()
    {
        using var context = new BunitContext();
        context.Services.AddSingleton<TimeProvider>(new FakeTimeProvider(Start));
        var load = Load();
        load.Stops = [load.Stops[0]];
        load.Stops[0].City = "";
        load.Stops[0].Address = "50 Patriot Drive";
        var component = context.Render<DispatchLoadCard>(p => p.Add(card => card.Load, load));
        Assert.Equal("50 Patriot Drive", component.Find(".dispatch-load__location").TextContent);
        Assert.Empty(component.FindAll(".dispatch-load__street"));

        load.Stops[0].PickedUpAt = Start.UtcDateTime;
        component.Render(p => p.Add(card => card.Load, load));
        Assert.Empty(component.FindAll(".dispatch-load__stop-details"));
    }

    [Fact]
    public async Task UnknownEmptyDistanceKeepsADashInDetailsWithoutInternalRoutePreparationTooltips()
    {
        using var context = new BunitContext();
        context.Services.AddSingleton<TimeProvider>(new FakeTimeProvider(Start));
        var load = Load();
        load.EmptyMiles = 43;
        load.EmptyMilesStatus = "ready";
        var component = context.Render<DispatchLoadCard>(p => p.Add(x => x.Load, load));
        Assert.Empty(component.FindAll(".dispatch-paper__financials"));
        await OpenDetailsAsync(context, component);
        var distance = component.Find(".dispatch-paper__empty-miles");
        Assert.Equal("Empty 43 mi", distance.TextContent);
        Assert.Equal("Planned road miles from the previous load's delivery to this pickup", distance.GetAttribute("title"));

        foreach (var status in new[] { "pending", "unknown", "" })
        {
            load.EmptyMiles = null;
            load.EmptyMilesStatus = status;
            component.Render(p => p.Add(x => x.Load, load));
            distance = component.Find(".dispatch-paper__empty-miles");
            Assert.Equal("Empty —", distance.TextContent);
            Assert.Null(distance.GetAttribute("title"));
            Assert.DoesNotContain("The server is preparing", component.Markup);
            Assert.DoesNotContain("unambiguous previous load", component.Markup);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CurrentAndFutureLoadCardsShowAndCopyTheExactOrderNumber(bool current)
    {
        using var context = new BunitContext();
        context.Services.AddSingleton<TimeProvider>(new FakeTimeProvider(Start));
        var load = Load();
        load.OrderNumber = "ORD-7391 / A";
        context.JSInterop.SetupVoid("navigator.clipboard.writeText", load.OrderNumber).SetVoidResult();
        var component = context.Render<DispatchLoadCard>(parameters => parameters.Add(card => card.Load, load).Add(card => card.Current, current));
        var button = component.Find("button[aria-label='Copy order number']");
        Assert.Contains($"Order {load.OrderNumber}", button.TextContent);
        Assert.Equal("button", component.Find(".dispatch-load__number").GetAttribute("type"));
        Assert.StartsWith("Open load ", component.Find(".dispatch-load__number").GetAttribute("aria-label"));
        Assert.Equal(3, component.FindAll(".dispatch-load__stop").Count);

        await button.ClickAsync(new MouseEventArgs());

        var invocation = Assert.Single(context.JSInterop.Invocations);
        Assert.Equal("navigator.clipboard.writeText", invocation.Identifier);
        Assert.Equal(load.OrderNumber, Assert.Single(invocation.Arguments));
        Assert.Single(component.FindAll(".dispatch-load__copy-icon.is-copied"));
        Assert.Equal("Copied", component.Find("button[aria-label='Copy order number']").GetAttribute("title"));
        Assert.Empty(component.FindAll("[role='status'], [role='alert']"));
        Assert.Empty(component.FindAll(".dispatch-load__summary, .dispatch-load__metrics"));
    }

    [Fact]
    public async Task FailedOrderCopyShowsRecoverableFeedbackAndAnotherLoadClearsIt()
    {
        using var context = new BunitContext();
        context.Services.AddSingleton<TimeProvider>(new FakeTimeProvider(Start));
        var load = Load();
        load.OrderNumber = "ORD-7391";
        context.JSInterop.SetupVoid("navigator.clipboard.writeText", load.OrderNumber).SetException(new JSException("Clipboard unavailable"));
        var component = context.Render<DispatchLoadCard>(parameters => parameters.Add(card => card.Load, load));
        await component.Find("button[aria-label='Copy order number']").ClickAsync(new MouseEventArgs());
        Assert.Equal("Could not copy. Please try again.", component.Find("[role='alert']").TextContent);

        var next = Load();
        next.OrderNumber = "ORD-NEW";
        component.Render(parameters => parameters.Add(card => card.Load, next));
        Assert.Empty(component.FindAll("[role='alert'], .dispatch-load__copy-icon.is-copied"));
        Assert.Contains(next.OrderNumber, component.Find("button[aria-label='Copy order number']").TextContent);
        next.OrderNumber = " ";
        component.Render(parameters => parameters.Add(card => card.Load, next));
        Assert.Empty(component.FindAll("button[aria-label='Copy order number']"));
    }

    [Fact]
    public async Task EveryStopShowsItsOwnAppointmentLocalEtaAndLatenessInSequence()
    {
        await using var context = new BunitContext();
        context.Services.AddSingleton<TimeProvider>(new FakeTimeProvider(Start));
        var load = Load();
        var expected = load.Stops.OrderBy(stop => stop.Sequence).ToArray();
        load.Stops.Reverse();
        var wrongLoad = new StopEta(expected[0].Id, Start.AddDays(20), "UTC", null, 999, 0, 0) { DispatchId = Guid.NewGuid() };
        load.Eta = load.Eta! with { Stops = new[] { wrongLoad }.Concat(load.Eta.Stops.Reverse()).ToArray() };

        var component = context.Render<DispatchLoadCard>(parameters => parameters.Add(card => card.Load, load).Add(card => card.Current, true));
        var stops = component.FindAll(".dispatch-load__stop");

        Assert.Equal(3, stops.Count);
        Assert.Equal(expected.Select(stop => stop.Id.ToString()), stops.Select(stop => stop.GetAttribute("data-stop-id")));
        Assert.All(stops, stop => Assert.DoesNotContain("Arrival time zone", stop.TextContent));
        Assert.Contains("Sep 9 · 09:00 AM", stops[0].TextContent);
        Assert.Contains("Sep 8 · 08:00 AM", stops[0].TextContent);
        Assert.Contains("Sep 8 · 10:00 AM", stops[1].TextContent);
        Assert.Contains("Sep 8 · 12:00 PM", stops[2].TextContent);
        Assert.Contains("Intermediate warehouse", stops[1].TextContent);
        Assert.Contains("City 2, ON", stops[1].TextContent);
        Assert.Single(stops[1].QuerySelectorAll(".arrival-estimate__late"));
        Assert.Contains("Late by 25m", stops[1].TextContent);
        Assert.Empty(stops[0].QuerySelectorAll(".arrival-estimate__late"));
        Assert.Empty(stops[2].QuerySelectorAll(".arrival-estimate__late"));
        Assert.Single(component.FindAll(".dispatch-load--current"));
        Assert.Empty(component.FindAll(".dispatch-load__summary, .dispatch-load__metrics"));
        await OpenDetailsAsync(context, component);
        Assert.Contains("900 mi", component.Find(".dispatch-paper__financials").TextContent);
        Assert.Contains("2,500.00 CAD", component.Find(".dispatch-paper__financials").TextContent);
        var detailedStops = component.FindAll(".dispatch-load-dialog .dispatch-load__stop");
        Assert.Equal(expected.Select(stop => stop.Id.ToString()), detailedStops.Select(stop => stop.GetAttribute("data-stop-id")));
        Assert.All(detailedStops, stop => Assert.Contains("Arrival time zone UTC-04:00", stop.TextContent));
        Assert.Single(detailedStops[1].QuerySelectorAll(".arrival-estimate__late"));
        Assert.Contains("Late by 25m", detailedStops[1].TextContent);
    }

    [Fact]
    public void MissingOrWrongLoadAndStopIdentitiesNeverUseTheFirstForecast()
    {
        using var context = new BunitContext();
        context.Services.AddSingleton<TimeProvider>(new FakeTimeProvider(Start));
        var load = Load();
        load.Stops[0].Id = Guid.Empty;
        load.Eta = load.Eta! with
        {
            Stops = [
                new(Guid.Empty, Start, "UTC", null, 0, 0, 0) { DispatchId = load.Id },
                new(load.Stops[1].Id, Start, "UTC", null, 0, 0, 0) { DispatchId = Guid.NewGuid() },
                new(load.Stops[2].Id, Start, "UTC", null, 0, 0, 0) { DispatchId = Guid.Empty },
                new(Guid.NewGuid(), Start, "UTC", null, 0, 0, 0) { DispatchId = load.Id }
            ]
        };
        var component = context.Render<DispatchLoadCard>(parameters => parameters.Add(card => card.Load, load));

        Assert.Equal(3, component.FindAll(".dispatch-load__eta-missing").Count);
        Assert.DoesNotContain("local ·", component.Markup);
        Assert.Empty(component.FindAll(".arrival-estimate__ontime"));
        Assert.Empty(component.FindAll(".arrival-estimate__late"));
    }

    [Fact]
    public void PerStopPreviousEstimatesSurviveUpdatingOnlyWithinTheDisplayGrace()
    {
        using var context = new BunitContext();
        var clock = new FakeTimeProvider(Start);
        context.Services.AddSingleton<TimeProvider>(clock);
        var load = Load();
        var component = context.Render<DispatchLoadCard>(parameters => parameters.Add(card => card.Load, load));
        Assert.Equal(2, component.FindAll(".arrival-estimate__ontime").Count);
        var previousMarkup = component.Markup;
        clock.Advance(TimeSpan.FromMinutes(3));
        load.Eta = load.Eta! with { Stops = [], RouteUpdatePending = true };
        component.Render(parameters => parameters.Add(card => card.Load, load));

        Assert.Equal(previousMarkup, component.Markup);
        Assert.DoesNotContain("Updating", component.Markup);
        Assert.Contains("Sep 8 · 08:00 AM", component.Markup);
        Assert.Contains("Sep 8 · 10:00 AM", component.Markup);
        Assert.Contains("Sep 8 · 12:00 PM", component.Markup);
        Assert.Empty(component.FindAll(".dispatch-load__eta-missing"));
        Assert.Equal(2, component.FindAll(".arrival-estimate__ontime").Count);
        Assert.Single(component.FindAll(".arrival-estimate__late"));

        clock.Advance(TimeSpan.FromMinutes(15));
        component.Render(parameters => parameters.Add(card => card.Load, load));
        Assert.Equal(3, component.FindAll(".dispatch-load__eta-missing").Count);
        Assert.DoesNotContain("local ·", component.Markup);
        Assert.Empty(component.FindAll(".arrival-estimate__ontime"));
    }

    [Fact]
    public void CompletedStopsKeepTheirStatusWithoutActualDatesOrRetainedEta()
    {
        using var context = new BunitContext();
        context.Services.AddSingleton<TimeProvider>(new FakeTimeProvider(Start));
        var load = Load();
        var component = context.Render<DispatchLoadCard>(parameters => parameters.Add(card => card.Load, load));
        load.Stops[0].PickedUpAt = Start.AddHours(-3).UtcDateTime;
        load.Stops[1].DepartedAt = Start.AddHours(-2).UtcDateTime;
        load.Stops[2].DeliveredAt = Start.AddHours(-1).UtcDateTime;
        component.Render(parameters => parameters.Add(card => card.Load, load));

        Assert.Equal(3, component.FindAll(".dispatch-load__stop--completed").Count);
        Assert.All(component.FindAll(".dispatch-load__stop"), stop => Assert.Contains("Completed", stop.TextContent));
        Assert.DoesNotContain("Actual pickup", component.Markup);
        Assert.DoesNotContain("Actual departure", component.Markup);
        Assert.DoesNotContain("Actual delivery", component.Markup);
        Assert.DoesNotContain("09:00 UTC", component.Markup);
        Assert.DoesNotContain("10:00 UTC", component.Markup);
        Assert.DoesNotContain("11:00 UTC", component.Markup);
        Assert.DoesNotContain("ETA", component.Markup);
        Assert.Empty(component.FindAll(".dispatch-load__eta-missing"));
        Assert.Empty(component.FindAll(".arrival-estimate__late"));
        Assert.Empty(component.FindAll(".arrival-estimate__ontime"));

        load.Eta = load.Eta! with { Stops = [], RouteUpdatePending = true };
        component.Render(parameters => parameters.Add(card => card.Load, load));
        Assert.DoesNotContain("Updating", component.Markup);
        Assert.DoesNotContain("ETA", component.Markup);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CurrentAndFutureCardsShowOnlyTheMatchingPickupAndDeliveryReferencesWithoutRequests(bool current)
    {
        var requests = 0;
        await using var context = new ClientComponentContext((_, _) =>
        {
            requests++;
            throw new InvalidOperationException("Appointment references must use the existing stop notes.");
        });
        context.Services.AddSingleton<TimeProvider>(new FakeTimeProvider(Start));
        var load = Load();
        load.Stops = [load.Stops[0], load.Stops[2]];
        const string notes = "Shipper appointment confirmation number: PU123456. Receiver appointment confirmation number: DL654321. Shipper BOL: 42845601. Commodity: STEEL COILS. Service for Load sentinel.";
        foreach (var stop in load.Stops) stop.Notes = notes;
        load.Stops[0].StopNo = "PU-REF / 123";
        load.Stops[1].StopNo = "DL-REF / 456";
        if (current) load.Stops[0].PickedUpAt = Start.AddHours(-3).UtcDateTime;

        var component = context.Render<DispatchLoadCard>(parameters => parameters.Add(card => card.Load, load).Add(card => card.Current, current));
        Assert.Empty(component.FindAll(".dispatch-load__appointment-reference, .dispatch-load__reference-number"));
        await OpenDetailsAsync(context, component);
        var stops = component.FindAll(".dispatch-load-dialog .dispatch-load__stop");

        Assert.Equal(2, stops.Count);
        Assert.Equal("Appt # PU123456", stops[0].QuerySelector(".dispatch-load__appointment-reference")!.TextContent.Trim());
        Assert.Equal("Appt # DL654321", stops[1].QuerySelector(".dispatch-load__appointment-reference")!.TextContent.Trim());
        Assert.Equal("Ref # PU-REF / 123", stops[0].QuerySelector(".dispatch-load__reference-number")!.TextContent.Trim());
        Assert.Equal("Ref # DL-REF / 456", stops[1].QuerySelector(".dispatch-load__reference-number")!.TextContent.Trim());
        Assert.DoesNotContain("DL654321", stops[0].QuerySelector(".dispatch-load__appointment-reference")!.TextContent);
        Assert.DoesNotContain("PU123456", stops[1].QuerySelector(".dispatch-load__appointment-reference")!.TextContent);
        Assert.DoesNotContain("DL-REF / 456", stops[0].QuerySelector(".dispatch-load__reference-number")!.TextContent);
        Assert.DoesNotContain("PU-REF / 123", stops[1].QuerySelector(".dispatch-load__reference-number")!.TextContent);
        Assert.Equal(new[] { "1", "2" }, stops.Select(stop => stop.QuerySelector(".dispatch-load__stop-number")!.TextContent.Trim()));
        Assert.Equal(new[] { "Stop 1", "Stop 2" }, stops.Select(stop => stop.QuerySelector(".dispatch-load__stop-number")!.GetAttribute("aria-label")));
        var referenceText = string.Join(" ", stops.SelectMany(stop => stop.QuerySelectorAll(
            ".dispatch-load__appointment-reference, .dispatch-load__reference-number")).Select(reference => reference.TextContent));
        Assert.DoesNotContain("42845601", referenceText);
        Assert.DoesNotContain("STEEL COILS", referenceText);
        Assert.DoesNotContain("Service for Load sentinel", referenceText);
        Assert.Contains(notes, component.Find(".dispatch-load-dialog details .dispatch-paper__notes").TextContent);
        Assert.DoesNotContain("Actual pickup", component.Markup);
        Assert.Equal(current, stops[0].ClassList.Contains("dispatch-load__stop--completed"));
        Assert.Equal(0, requests);
    }

    [Fact]
    public async Task ChangedNotesAndJobRefreshReferencesAndMissingNotesClearThem()
    {
        await using var context = new BunitContext();
        context.Services.AddSingleton<TimeProvider>(new FakeTimeProvider(Start));
        var load = Load();
        var stop = load.Stops[0];
        load.Stops = [stop];
        stop.Notes = "Shipper appointment confirmation number: PU123456. Receiver appointment confirmation number: DL654321.";
        var component = context.Render<DispatchLoadCard>(parameters => parameters.Add(card => card.Load, load));
        await OpenDetailsAsync(context, component);
        Assert.Contains("PU123456", component.Find(".dispatch-load__appointment-reference").TextContent);

        stop.Notes = "Shipper appointment confirmation number: PU987654. Receiver appointment confirmation number: DL123456.";
        component.Render(parameters => parameters.Add(card => card.Load, load));
        Assert.Contains("PU987654", component.Find(".dispatch-load__appointment-reference").TextContent);
        Assert.DoesNotContain("PU123456", component.Markup);

        stop.Job = "Delivery";
        component.Render(parameters => parameters.Add(card => card.Load, load));
        Assert.Contains("DL123456", component.Find(".dispatch-load__appointment-reference").TextContent);
        Assert.DoesNotContain("PU987654", component.Find(".dispatch-load-dialog .dispatch-load__appointment-reference").TextContent);

        stop.Job = "unknown";
        component.Render(parameters => parameters.Add(card => card.Load, load));
        Assert.Empty(component.FindAll(".dispatch-load__appointment-reference"));

        stop.Job = "Delivery";
        component.Render(parameters => parameters.Add(card => card.Load, load));
        Assert.Contains("DL123456", component.Find(".dispatch-load__appointment-reference").TextContent);
        stop.Notes = string.Empty;
        component.Render(parameters => parameters.Add(card => card.Load, load));
        Assert.Empty(component.FindAll(".dispatch-load__appointment-reference"));
        Assert.DoesNotContain("DL123456", component.Markup);
        Assert.Single(component.FindAll(".dispatch-load-dialog .dispatch-load__stop-number"));
    }

    [Fact]
    public async Task StructuredStopReferencesUpdateAndClearWithoutBecomingAppointmentNumbers()
    {
        await using var context = new BunitContext();
        context.Services.AddSingleton<TimeProvider>(new FakeTimeProvider(Start));
        var load = Load();
        var stop = load.Stops[0];
        load.Stops = [stop];
        stop.StopNo = "PU-REF / 123";
        var component = context.Render<DispatchLoadCard>(parameters => parameters.Add(card => card.Load, load));
        await OpenDetailsAsync(context, component);
        Assert.Equal("Ref # PU-REF / 123", component.Find(".dispatch-load__reference-number").TextContent.Trim());
        Assert.Empty(component.FindAll(".dispatch-load__appointment-reference"));

        stop.StopNo = "PU-REF / 789";
        component.Render(parameters => parameters.Add(card => card.Load, load));
        Assert.Equal("Ref # PU-REF / 789", component.Find(".dispatch-load__reference-number").TextContent.Trim());
        Assert.DoesNotContain("PU-REF / 123", component.Markup);

        stop.StopNo = " ";
        component.Render(parameters => parameters.Add(card => card.Load, load));
        Assert.Empty(component.FindAll(".dispatch-load__reference-number"));
        Assert.DoesNotContain("PU-REF / 789", component.Markup);
        Assert.Empty(component.FindAll(".dispatch-load__appointment-reference"));
    }

    [Fact]
    public void PresentationMakesNoRequestsAndCannotRetainAnotherLoadsEstimate()
    {
        var requests = 0;
        using var context = new ClientComponentContext((_, _) =>
        {
            requests++;
            throw new InvalidOperationException("Dispatch Cards must use the board forecast.");
        });
        context.Services.AddSingleton<TimeProvider>(new FakeTimeProvider(Start));
        var load = Load();
        var component = context.Render<DispatchLoadCard>(parameters => parameters.Add(card => card.Load, load));
        load.Id = Guid.NewGuid();
        load.Eta = null;
        component.Render(parameters => parameters.Add(card => card.Load, load));

        Assert.Equal(3, component.FindAll(".dispatch-load__eta-missing").Count);
        Assert.DoesNotContain("local ·", component.Markup);
        Assert.Equal(0, requests);
    }

    private static async Task OpenDetailsAsync(BunitContext context, IRenderedComponent<DispatchLoadCard> component)
    {
        context.JSInterop.SetupModule("./js/generated/shared/loadDialog.js").Mode = JSRuntimeMode.Loose;
        await component.Find(".dispatch-load__footer .dispatch-load__details").ClickAsync(new MouseEventArgs());
        Assert.Single(component.FindAll("dialog.dispatch-load-dialog"));
    }

    private static DispatchResponse Load()
    {
        var load = new DispatchResponse
        {
            Id = Guid.NewGuid(), LoadNumber = 1000, Status = "in_transit", LoadedMiles = 900, EmptyMiles = 100,
            TotalMiles = 1000, Price = 2500, Currency = "CAD", LoadedRatePerMile = 2.78m, TotalRatePerMile = 2.5m,
            Stops = Enumerable.Range(1, 3).Select(index => new DispatchStopResponse
            {
                Id = Guid.NewGuid(), Sequence = index, Job = index == 1 ? "Pickup" : "Delivery",
                Name = index == 2 ? "Intermediate warehouse" : $"Facility {index}", City = $"City {index}", Province = "ON",
                ScheduledDate = new(2026, 9, 9), ScheduledTime = new(9, 0)
            }).ToList()
        };
        load.Eta = new(Start.UtcDateTime, Start.AddMinutes(2).UtcDateTime,
            load.Stops.Select((stop, index) => new StopEta(stop.Id, Start.AddHours(index * 2).ToOffset(TimeSpan.FromHours(-4)),
                "America/Toronto", null, index == 1 ? 25 : 0, 60, 0) { DispatchId = load.Id }).ToArray(), null, []);
        return load;
    }
}
