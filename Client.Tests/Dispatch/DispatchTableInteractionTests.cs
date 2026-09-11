using Client.Shared.Dispatch.DispatchLoadDialog;
using Client.Models.DTO.Dispatch;
using Client.Shared.Dispatch;
using Bunit;
using Client.Pages.Dispatch;
using Client.Shared;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;

namespace Client.Tests.Dispatch;

[Trait("Category", "Dispatch")]
[Trait("Kind", "Component")]
public sealed class DispatchTableInteractionTests
{
    [Fact]
    public async Task OpenLoadAndOrdinaryRowClickUseOneExistingDataDialog()
    {
        await using var context = Context();
        var load = DispatchFinancialViewsTests.Load();
        var truck = new TruckDispatchBoardResponse { Key = "truck", Dispatches = [load] };
        var table = context.Render<DispatchTable>(p => p.Add(view => view.Trucks, [truck])
            .Add(view => view.LoadPhase, (_, _) => "Current"));
        Assert.Empty(table.FindAll("dialog, details"));
        var open = table.Find("button.dispatch-table__open");
        Assert.Equal("dialog", open.GetAttribute("aria-haspopup"));
        Assert.StartsWith("Open load ", open.GetAttribute("aria-label"));
        await open.ClickAsync(new MouseEventArgs());

        Assert.Same(load, table.FindComponent<DispatchLoadDialog>().Instance.Load);
        Assert.Same(truck, table.FindComponent<DispatchLoadDialog>().Instance.Truck);
        Assert.Equal("Current", table.FindComponent<DispatchLoadDialog>().Instance.Phase);
        Assert.Single(table.FindAll("dialog.dispatch-load-dialog"));
        await table.Find("button[aria-label='Close load details']").ClickAsync(new MouseEventArgs());
        Assert.Empty(table.FindAll("dialog"));

        await table.Find("tr.dispatch-table__row").ClickAsync(new MouseEventArgs());
        Assert.Single(table.FindAll("dialog.dispatch-load-dialog"));
        Assert.Same(load, table.FindComponent<DispatchLoadDialog>().Instance.Load);
    }

    [Fact]
    public async Task MapNavigationAndModifiedRowClicksDoNotOpenDialog()
    {
        await using var context = Context();
        var load = DispatchFinancialViewsTests.Load();
        var table = context.Render<DispatchTable>(p => p.Add(view => view.Trucks,
            [new TruckDispatchBoardResponse { Key = "truck", Dispatches = [load] }]));
        var map = table.Find("a.dispatch-table__map");
        Assert.Contains($"dispatchId={load.Id}", map.GetAttribute("href"));
        Assert.Equal("a", map.LocalName);
        Assert.DoesNotContain(context.JSInterop.Invocations, invocation => invocation.Identifier == "import");
        Assert.Empty(table.FindAll("dialog"));
        foreach (var args in new[] { new MouseEventArgs { CtrlKey = true }, new MouseEventArgs { MetaKey = true },
            new MouseEventArgs { ShiftKey = true }, new MouseEventArgs { AltKey = true }, new MouseEventArgs { Button = 1 } })
            await table.Find("tr.dispatch-table__row").ClickAsync(args);
        Assert.Empty(table.FindAll("dialog"));
    }

    [Fact]
    public async Task SelectionRefreshesFromExistingRowsAndClearsWhenPageOrScopeChanges()
    {
        await using var context = Context();
        var first = DispatchFinancialViewsTests.Load();
        var table = context.Render<DispatchTable>(p => p.Add(view => view.Trucks,
            [new TruckDispatchBoardResponse { Key = "truck", Dispatches = [first] }]));
        await table.Find("button.dispatch-table__open").ClickAsync(new MouseEventArgs());
        var updated = DispatchFinancialViewsTests.Load();
        updated.Id = first.Id;
        updated.CustomerName = "Updated customer";
        var trucks = new[] { new TruckDispatchBoardResponse { Key = "truck", Dispatches = [updated] } };
        table.Render(p => p.Add(view => view.Trucks, trucks));
        Assert.Same(updated, table.FindComponent<DispatchLoadDialog>().Instance.Load);
        Assert.Contains("Updated customer", table.Find("dialog").TextContent);
        table.Render(p => p.Add(view => view.Trucks, []));
        Assert.Empty(table.FindAll("dialog"));
        table.Render(p => p.Add(view => view.Trucks, trucks));
        Assert.Empty(table.FindAll("dialog"));
        await table.Find("button.dispatch-table__open").ClickAsync(new MouseEventArgs());
        table.Render(p => p.Add(view => view.Completed, true));
        Assert.Empty(table.FindAll("dialog"));
    }

    [Fact]
    public async Task AllPickupAndDeliveryEntriesRemainVisibleInOrderWithoutRowDisclosures()
    {
        await using var context = Context();
        var load = DispatchFinancialViewsTests.Load();
        load.Stops.Add(new() { Id = Guid.NewGuid(), Sequence = 3, Job = "Pickup", City = "Second origin" });
        load.Stops.Add(new() { Id = Guid.NewGuid(), Sequence = 4, Job = "Delivery", City = "Second destination" });
        load.Stops.Reverse();
        var table = context.Render<DispatchTable>(p => p.Add(view => view.Trucks,
            [new TruckDispatchBoardResponse { Key = "truck", Dispatches = [load] }]));
        Assert.Equal(["Origin", "Second origin"], table.FindAll(".dispatch-table__stop.is-pickup .dispatch-table__stop-entry > strong").Select(e => e.TextContent));
        Assert.Equal(["Destination", "Second destination"], table.FindAll(".dispatch-table__stop.is-delivery .dispatch-table__stop-entry > strong").Select(e => e.TextContent));
        Assert.Single(table.FindAll(".dispatch-table__stop-completed"));
        Assert.Empty(table.FindAll("details"));
        Assert.Equal(8, table.FindAll("tbody tr:first-child td").Count);
        load.Status = "completed";
        table.Render();
        Assert.Equal(4, table.FindAll(".dispatch-table__stop-completed").Count);
    }

    private static BunitContext Context()
    {
        var context = new BunitContext();
        context.Services.AddSingleton(TimeProvider.System);
        context.JSInterop.SetupModule("./js/generated/shared/loadDialog.js").Mode = JSRuntimeMode.Loose;
        return context;
    }
}
