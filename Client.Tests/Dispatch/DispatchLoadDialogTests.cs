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
public sealed class DispatchLoadDialogTests
{
    [Fact]
    public async Task OpensOnceKeepsHistoricalDataAndClosesExactlyOnce()
    {
        using var context = new BunitContext();
        context.Services.AddSingleton(TimeProvider.System);
        var module = context.JSInterop.SetupModule("./js/generated/shared/loadDialog.js");
        module.Mode = JSRuntimeMode.Loose;
        var load = DispatchFinancialViewsTests.Load();
        load.OrderNumber = "ORDER-23";
        load.Stops[0].Address = "123 Warehouse Road";
        load.Stops[0].StopNo = "REF-17";
        load.Stops[0].Commodity = "Apples";
        load.Stops[0].Weight = 42000;
        load.Stops[0].WeightUnit = "lb";
        load.Stops[0].Pieces = 120;
        load.Stops[0].Pallets = 20;
        load.Stops[0].ZipCode = "29715";
        load.Stops[0].Country = "US";
        load.Stops[0].Temperature = "34";
        load.Stops[0].TemperatureUnit = "F";
        load.Stops[0].Notes = "Keep refrigerated";
        var closed = 0;
        var component = context.Render<DispatchLoadDialog>(parameters => parameters
            .Add(dialog => dialog.Load, load).Add(dialog => dialog.Phase, "Current")
            .Add(dialog => dialog.Truck, new TruckDispatchBoardResponse { TruckNumber = "Live Truck", DriverName = "Live Driver", TrailerNumber = "Live Trailer" })
            .Add(dialog => dialog.Closed, () => closed++));
        Assert.Single(module.Invocations, invocation => invocation.Identifier == "show");
        Assert.Equal($"dispatch-load-dialog-{load.Id}-title", component.Find("dialog").GetAttribute("aria-labelledby"));
        Assert.True(component.Find(".dispatch-paper__identity").HasAttribute("autofocus"));
        Assert.Equal(2, component.FindAll(".dispatch-load__stop-details").Count);
        Assert.All(component.FindAll(".dispatch-load__stop-details"), details =>
        {
            Assert.False(details.HasAttribute("open"));
            Assert.Equal("More details", details.QuerySelector("summary")!.TextContent);
        });
        var pickup = component.Find(".dispatch-paper__stops > li.is-pickup .dispatch-load__stop");
        Assert.Equal("123 Warehouse Road", pickup.QuerySelector(":scope > .dispatch-load__street")!.TextContent);
        Assert.NotNull(pickup.QuerySelector(":scope > .dispatch-load__location"));
        Assert.NotNull(pickup.QuerySelector(":scope > .dispatch-load__stop-times"));
        Assert.Contains("REF-17", pickup.QuerySelector("details .dispatch-load__reference-number")!.TextContent);
        Assert.Contains("Apples", pickup.QuerySelector("details .dispatch-paper__stop-metadata")!.TextContent);
        Assert.Contains("Keep refrigerated", pickup.QuerySelector("details .dispatch-paper__notes")!.TextContent);
        Assert.Single(pickup.QuerySelectorAll(".dispatch-load__street"));
        foreach (var text in new[] { "54777", "Historic Driver", "ARCHIVE-1", "ORDER-23", "REF-17", "123 Warehouse Road", "Apples", "42,000", "120", "20", "29715", "US", "34", "Keep refrigerated", "17.23 CAD", "4.56 CAD" })
            Assert.Contains(text, component.Markup);
        Assert.DoesNotContain("Live Driver", component.Markup);
        component.Render(parameters => parameters.Add(dialog => dialog.Load, load));
        Assert.Single(module.Invocations, invocation => invocation.Identifier == "show");
        await component.Find("button[aria-label='Close load details']").ClickAsync(new MouseEventArgs());
        await component.Find("dialog").TriggerEventAsync("onclose", EventArgs.Empty);
        Assert.Equal(1, closed);
        Assert.Single(module.Invocations, invocation => invocation.Identifier == "close");
    }

    [Fact]
    public void CompletedOverrideNeverDisplaysLiveEtaOrCurrentPhase()
    {
        using var context = new BunitContext();
        context.Services.AddSingleton(TimeProvider.System);
        context.JSInterop.SetupModule("./js/generated/shared/loadDialog.js").Mode = JSRuntimeMode.Loose;
        var load = DispatchFinancialViewsTests.Load();
        foreach (var stop in load.Stops) stop.PickedUpAt = null;
        var component = context.Render<DispatchLoadDialog>(parameters => parameters.Add(dialog => dialog.Load, load)
            .Add(dialog => dialog.Completed, true).Add(dialog => dialog.Phase, "Current"));
        Assert.Equal("Completed", component.Find(".dispatch-paper__tab-status").TextContent);
        Assert.DoesNotContain("Current", component.Find(".dispatch-paper__identity").TextContent);
        Assert.Equal(2, component.FindAll(".dispatch-load__completed").Count);
        Assert.Empty(component.FindAll(".dispatch-cycle-forecast, .dispatch-load__eta-missing, .stop-hours"));
    }
}
