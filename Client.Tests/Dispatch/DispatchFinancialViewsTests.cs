using Client.Models.DTO.Dispatch;
using Bunit;
using Client.Pages.Dispatch;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;

namespace Client.Tests.Dispatch;

[Trait("Category", "Dispatch")]
[Trait("Kind", "Component")]
public sealed class DispatchFinancialViewsTests
{
    [Fact]
    public async Task AllViewsFormatServerFinancialsAndPreserveCompletedPickup()
    {
        using var context = new BunitContext();
        context.Services.AddSingleton(TimeProvider.System);
        context.JSInterop.Mode = JSRuntimeMode.Loose;
        context.JSInterop.SetupModule("./js/generated/shared/loadDialog.js").Mode = JSRuntimeMode.Loose;
        var load = Load();
        var trucks = new[] { new TruckDispatchBoardResponse { Key = "historic", Dispatches = [load] } };
        var cards = context.Render<DispatchLoadCard>(parameters => parameters.Add(card => card.Load, load));
        Assert.Empty(cards.FindAll(".dispatch-load__metrics, .dispatch-paper__financials"));
        Assert.Equal(2, cards.FindAll(".dispatch-load__stop").Count);
        Assert.Contains("Completed", cards.Find(".dispatch-load__stop").TextContent);
        await cards.Find(".dispatch-load__details").ClickAsync(new MouseEventArgs());
        AssertFinancials(cards.Find(".dispatch-paper__financials").TextContent);

        var table = context.Render<DispatchTable>(parameters => parameters.Add(view => view.Trucks, trucks));
        AssertFinancials(table.Markup);
        Assert.Contains("Pickup", table.Markup);
        Assert.Contains("Origin", table.Markup);
        Assert.Contains("Destination", table.Markup);
        Assert.Single(table.FindAll(".dispatch-table__stop-completed"));

        var papers = context.Render<DispatchPapers>(parameters => parameters.Add(view => view.Trucks, trucks));
        Assert.Contains("1,000.00 CAD", papers.Find(".dispatch-paper__tab-financials").TextContent);
        await papers.Find(".dispatch-paper__tab").ClickAsync(new MouseEventArgs());
        AssertFinancials(papers.Find(".dispatch-paper__financials").TextContent);
        Assert.Equal(2, papers.FindAll(".dispatch-paper__stops li").Count);
        Assert.Single(papers.FindAll(".dispatch-load__completed"));
    }

    [Fact]
    public void TableGroupsHistoricalTruckDriverAndKeepsEightFinancialColumns()
    {
        using var context = new BunitContext();
        var load = Load();
        load.Status = "completed";
        load.OrderNumber = "ORDER-19";
        load.Stops[0].Address = "123 Origin Street";
        var trucks = new[] { new TruckDispatchBoardResponse { Key = "truck", TruckNumber = "Live Truck", DriverName = "Live Driver", TrailerNumber = "Live Trailer", Dispatches = [load] } };
        var table = context.Render<DispatchTable>(parameters => parameters.Add(view => view.Trucks, trucks));

        Assert.Equal(["Load / status", "Truck / driver", "Pickup", "Delivery", "Miles", "Rate", "Loaded RPM", "Total RPM"],
            table.FindAll("thead th").Select(cell => cell.TextContent).ToArray());
        Assert.Equal(8, table.FindAll("tbody tr:first-child td").Count);
        var equipment = table.Find(".dispatch-table__equipment").TextContent;
        foreach (var text in new[] { "54777", "Historic Driver", "Trailer ARCHIVE-1" }) Assert.Contains(text, equipment);
        Assert.Empty(table.FindAll("details"));
        Assert.DoesNotContain("Live Driver", table.Markup);
        Assert.Contains("ORDER-19", table.Find(".dispatch-table__load").TextContent);
        Assert.Contains("Customer", table.Find(".dispatch-table__load").TextContent);
        Assert.Contains("123 Origin Street", table.Find(".dispatch-table__stop").TextContent);
        Assert.Equal("Completed", table.Find(".dispatch-table__status").TextContent);
        Assert.Equal(2, table.FindAll(".dispatch-table__stop-completed").Count);
        Assert.Equal(["1,000.00 CAD", "17.23 CAD", "4.56 CAD"],
            table.FindAll("tbody .dispatch-table__money strong").Select(cell => cell.TextContent).ToArray());
        Assert.Single(table.FindAll(".dispatch-table__map"));
        Assert.Single(table.FindAll(".dispatch-table__truck .dispatch-table__map"));
        Assert.Empty(table.FindAll(".dispatch-table__equipment > .dispatch-table__map"));
    }

    [Fact]
    public async Task TableAndPapersDisplayCompleteAppointmentWindows()
    {
        using var context = new BunitContext();
        context.Services.AddSingleton(TimeProvider.System);
        context.JSInterop.Mode = JSRuntimeMode.Loose;
        context.JSInterop.SetupModule("./js/generated/shared/loadDialog.js").Mode = JSRuntimeMode.Loose;
        var load = Load();
        load.Stops[0].IsWindow = true;
        load.Stops[0].ScheduledDate = new(2026, 9, 9);
        load.Stops[0].ScheduledTime = new(7, 0);
        load.Stops[0].ScheduledDate2 = new(2026, 9, 9);
        load.Stops[0].ScheduledTime2 = new(14, 0);
        load.Stops[1].IsWindow = true;
        load.Stops[1].ScheduledDate = new(2026, 9, 10);
        load.Stops[1].ScheduledTime = new(23, 0);
        load.Stops[1].ScheduledDate2 = new(2026, 9, 11);
        load.Stops[1].ScheduledTime2 = new(5, 0);
        var trucks = new[] { new TruckDispatchBoardResponse { Key = "truck", Dispatches = [load] } };
        var expected = new[] { "Sep 9 · 07:00 AM – 02:00 PM", "Sep 10 · 11:00 PM – Sep 11 · 05:00 AM" };
        var table = context.Render<DispatchTable>(parameters => parameters.Add(view => view.Trucks, trucks));
        Assert.Equal(expected, table.FindAll(".dispatch-table__schedule").Select(schedule => schedule.TextContent).ToArray());
        var papers = context.Render<DispatchPapers>(parameters => parameters.Add(view => view.Trucks, trucks));
        await papers.Find(".dispatch-paper__tab").ClickAsync(new MouseEventArgs());
        foreach (var schedule in expected) Assert.Contains(schedule, papers.Find(".dispatch-paper__stops").TextContent);
    }

    [Fact]
    public async Task PapersOpenOneModalAndRetainSelectionWithoutReopeningWhenItsFolderChanges()
    {
        using var context = new BunitContext();
        context.Services.AddSingleton(TimeProvider.System);
        context.JSInterop.Mode = JSRuntimeMode.Loose;
        var dialog = context.JSInterop.SetupModule("./js/generated/shared/loadDialog.js");
        dialog.Mode = JSRuntimeMode.Loose;
        var first = Load(1375);
        var second = Load(1373);
        second.Status = "planned";
        second.Stops[0].PickedUpAt = null;
        var trucks = new[] { new TruckDispatchBoardResponse { Key = "truck", Dispatches = [first, second] } };
        var papers = context.Render<DispatchPapers>(parameters => parameters.Add(view => view.Trucks, trucks));
        Assert.Equal(3, papers.FindAll(".dispatch-paper-column").Count);
        Assert.Empty(papers.FindAll(".dispatch-load-dialog"));

        await papers.FindAll(".dispatch-paper__tab").Single(tab => tab.TextContent.Contains("1373", StringComparison.Ordinal)).ClickAsync(new MouseEventArgs());
        var document = papers.Find(".dispatch-load-dialog");
        Assert.Equal("DIALOG", document.TagName);
        Assert.Equal($"dispatch-load-dialog-{second.Id}", document.Id);
        Assert.Equal("-1", document.QuerySelector(".dispatch-paper__identity")?.GetAttribute("tabindex"));
        Assert.Single(dialog.Invocations, invocation => invocation.Identifier == "show");
        Assert.Empty(papers.FindAll(".dispatch-paper-column article"));
        Assert.Equal(4, document.QuerySelectorAll(".dispatch-paper__financials > div").Length);
        Assert.Equal(["1", "2"], document.QuerySelectorAll(".dispatch-load__stop-number").Select(marker => marker.TextContent).ToArray());
        Assert.Single(papers.FindAll(".dispatch-paper__tab[aria-expanded=true]"));
        Assert.All(papers.FindAll(".dispatch-paper__tab-content"), content => Assert.Equal(3, content.Children.Length));
        Assert.All(papers.FindAll(".dispatch-paper__tab-content"), content => Assert.NotNull(content.Children[0].QuerySelector(".dispatch-paper__tab-schedule")));

        second.Status = "in_transit";
        second.Stops[0].PickedUpAt = DateTime.UtcNow;
        papers.Render(parameters => parameters.Add(view => view.Trucks, trucks));
        Assert.Contains("1373", papers.Find(".dispatch-paper__title").TextContent);
        Assert.Contains("In transit", papers.Find(".dispatch-paper__title").TextContent);
        Assert.Single(papers.FindAll(".dispatch-load-dialog"));
        Assert.Contains("1373", papers.Find(".dispatch-paper-column--1 .dispatch-paper__tab[aria-expanded=true]").TextContent);
        Assert.Single(dialog.Invocations, invocation => invocation.Identifier == "show");

        await papers.FindAll(".dispatch-paper__tab").Single(tab => tab.TextContent.Contains("1375", StringComparison.Ordinal)).ClickAsync(new MouseEventArgs());
        Assert.Contains("1375", papers.Find(".dispatch-paper__title").TextContent);
        Assert.Single(papers.FindAll(".dispatch-load-dialog"));
        await papers.Find(".dispatch-load-dialog").TriggerEventAsync("onclose", EventArgs.Empty);
        Assert.Empty(papers.FindAll(".dispatch-load-dialog"));
        Assert.Empty(papers.FindAll(".dispatch-paper__tab[aria-expanded=true]"));
        Assert.DoesNotContain(context.JSInterop.Invocations, invocation => invocation.Identifier is "lockScroll" or "unlockScroll");
    }

    [Fact]
    public async Task PapersRemoveSelectedDocumentWhenSearchPageNoLongerContainsIt()
    {
        using var context = new BunitContext();
        context.Services.AddSingleton(TimeProvider.System);
        context.JSInterop.Mode = JSRuntimeMode.Loose;
        var dialog = context.JSInterop.SetupModule("./js/generated/shared/loadDialog.js");
        dialog.Mode = JSRuntimeMode.Loose;
        var load = Load();
        var papers = context.Render<DispatchPapers>(parameters => parameters.Add(view => view.Trucks,
            [new TruckDispatchBoardResponse { Key = "truck", Dispatches = [load] }]));
        await papers.Find(".dispatch-paper__tab").ClickAsync(new MouseEventArgs());
        Assert.Single(papers.FindAll(".dispatch-load-dialog"));
        papers.Render(parameters => parameters.Add(view => view.Trucks, []));
        Assert.Empty(papers.FindAll(".dispatch-load-dialog"));
        Assert.Empty(papers.FindAll(".dispatch-paper__tab"));
        papers.WaitForAssertion(() => Assert.Single(dialog.Invocations, invocation => invocation.Identifier == "dispose"));
    }

    [Fact]
    public async Task TableAndPapersKeepBoardPhaseSeparateFromActualStatusAndArchiveNeverClaimsCurrent()
    {
        using var context = new BunitContext();
        context.Services.AddSingleton(TimeProvider.System);
        context.JSInterop.Mode = JSRuntimeMode.Loose;
        context.JSInterop.SetupModule("./js/generated/shared/loadDialog.js").Mode = JSRuntimeMode.Loose;
        var current = Load(1375);
        var next = Load(1373);
        next.Status = "planned";
        next.Stops[0].PickedUpAt = null;
        var trucks = new[] { new TruckDispatchBoardResponse { Key = "truck", Dispatches = [current, next] } };
        string Phase(TruckDispatchBoardResponse truck, DispatchResponse load) => truck.Dispatches[0].Id == load.Id ? "Current" : "Next";
        var table = context.Render<DispatchTable>(parameters => parameters.Add(view => view.Trucks, trucks).Add(view => view.LoadPhase, Phase));
        Assert.Equal(["Current", "Next"], table.FindAll(".dispatch-table__phase").Select(badge => badge.TextContent).ToArray());
        Assert.Equal(["In transit", "Planned"], table.FindAll(".dispatch-table__status").Select(badge => badge.TextContent).ToArray());
        Assert.Single(table.FindAll("tr.is-current"));
        Assert.Single(table.FindAll("tr.is-next"));
        Assert.Contains("500 mi", table.Find(".dispatch-table__mileage-values").TextContent);
        Assert.Contains("550 mi", table.Find(".dispatch-table__mileage-values").TextContent);

        var papers = context.Render<DispatchPapers>(parameters => parameters.Add(view => view.Trucks, trucks).Add(view => view.LoadPhase, Phase));
        await papers.FindAll(".dispatch-paper__tab").Single(tab => tab.TextContent.Contains("1373", StringComparison.Ordinal)).ClickAsync(new MouseEventArgs());
        Assert.Equal("Next", papers.Find(".dispatch-paper__phase").TextContent);
        Assert.Equal("Planned", papers.Find(".dispatch-paper__title .dispatch-paper__tab-status").TextContent);
        Assert.Equal("Loaded miles", papers.Find(".dispatch-paper__financials > div:first-child dt").TextContent);
        Assert.Contains("550 mi total", papers.Find(".dispatch-paper__tab-miles").TextContent);

        current.Status = "completed";
        trucks[0].Dispatches = [current];
        table.Render(parameters => parameters.Add(view => view.Trucks, trucks).Add(view => view.Completed, true));
        Assert.Empty(table.FindAll(".dispatch-table__phase, tr.is-current, tr.is-next"));
        Assert.Equal("Completed", table.Find(".dispatch-table__status").TextContent);
        papers.Render(parameters => parameters.Add(view => view.Trucks, trucks).Add(view => view.Completed, true));
        await papers.Find(".dispatch-paper__tab").ClickAsync(new MouseEventArgs());
        Assert.Empty(papers.FindAll(".dispatch-paper__phase"));
        Assert.Equal("Completed", papers.Find(".dispatch-paper__title .dispatch-paper__tab-status").TextContent);
    }

    [Fact]
    public void CompletedLoadNeverShowsCurrentNextOrLiveEtaEvenWithoutStopTimestamps()
    {
        using var context = new BunitContext();
        context.Services.AddSingleton(TimeProvider.System);
        var load = Load();
        load.Status = "completed";
        foreach (var stop in load.Stops) stop.PickedUpAt = null;
        var card = context.Render<DispatchLoadCard>(parameters => parameters.Add(view => view.Load, load).Add(view => view.Current, true));
        Assert.Equal("Completed", card.Find(".dispatch-load__phase").TextContent);
        Assert.Equal("Completed", card.Find(".dispatch-load__status").TextContent);
        Assert.Equal(2, card.FindAll(".dispatch-load__stop--completed").Count);
        Assert.Empty(card.FindAll(".dispatch-load--current"));
        Assert.Empty(card.FindAll(".arrival-estimate, .dispatch-cycle-forecast, .dispatch-load__eta-missing"));
    }

    internal static DispatchResponse Load(int number = 1375) => new()
    {
        Id = Guid.NewGuid(), TruckId = Guid.NewGuid(), LoadNumber = number, TruckNumber = "54777", Status = "assigned",
        DriverName = "Historic Driver", TrailerNumber = "ARCHIVE-1", CustomerName = "Customer",
        Price = 1000, Currency = "cad", LoadedMiles = 500, EmptyMiles = 50, TotalMiles = 550,
        LoadedRatePerMile = 17.23m, TotalRatePerMile = 4.56m,
        Stops = [new() { Id = Guid.NewGuid(), Sequence = 1, Job = "Pick Up", City = "Origin", PickedUpAt = DateTime.UtcNow.AddDays(-2) },
            new() { Id = Guid.NewGuid(), Sequence = 2, Job = "Drop Off", City = "Destination" }]
    };

    private static void AssertFinancials(string text)
    {
        foreach (var value in new[] { "1,000.00 CAD", "17.23 CAD", "4.56 CAD", "500 mi", "50 mi", "550 mi" }) Assert.Contains(value, text);
    }
}
