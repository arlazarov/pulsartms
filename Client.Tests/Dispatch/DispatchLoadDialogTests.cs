using Bunit;
using Client.Models.DTO.Dispatch;
using Client.Pages.Dispatch;
using Client.Shared;
using Client.Shared.Dispatch;
using Client.Shared.Dispatch.DispatchLoadDialog;
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
    var module = context.JSInterop.SetupModule(
      "./js/generated/shared/loadDialog.js"
    );
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
    var component = context.Render<DispatchLoadDialog>(parameters =>
      parameters
        .Add(dialog => dialog.Load, load)
        .Add(dialog => dialog.Phase, "Current")
        .Add(
          dialog => dialog.Truck,
          new TruckDispatchBoardResponse
          {
            TruckNumber = "Live Truck",
            DriverName = "Live Driver",
            TrailerNumber = "Live Trailer",
          }
        )
        .Add(dialog => dialog.Closed, () => closed++)
    );
    Assert.Single(
      module.Invocations,
      invocation => invocation.Identifier == "show"
    );
    Assert.Equal(
      $"dispatch-load-dialog-{load.Id}-title",
      component.Find("dialog").GetAttribute("aria-labelledby")
    );
    Assert.True(
      component.Find(".dispatch-paper__identity").HasAttribute("autofocus")
    );
    Assert.Single(component.FindAll(".dispatch-load__more-details"));
    Assert.Empty(component.FindAll(".is-delivery details"));
    await component
      .Find(".dispatch-load__more-details")
      .ClickAsync(new MouseEventArgs());
    Assert.Contains("is-stop-view", component.Find("dialog").ClassName);
    Assert.Single(
      component.FindAll(".dispatch-paper__stops > li:not([hidden])")
    );
    Assert.Single(
      module.Invocations,
      invocation => invocation.Identifier == "rememberOverview"
    );
    var pickup = component.Find(
      ".dispatch-paper__stops > li.is-pickup .dispatch-load__stop"
    );
    Assert.Equal(
      "123 Warehouse Road",
      pickup.QuerySelector(":scope > .dispatch-load__street")!.TextContent
    );
    Assert.NotNull(pickup.QuerySelector(":scope > .dispatch-load__location"));
    Assert.NotNull(pickup.QuerySelector(":scope > .dispatch-load__stop-times"));
    Assert.Contains(
      "REF-17",
      pickup.QuerySelector(".dispatch-load__reference-number")!.TextContent
    );
    Assert.Contains(
      "Apples",
      pickup.QuerySelector(".dispatch-paper__stop-metadata")!.TextContent
    );
    Assert.Contains(
      "Keep refrigerated",
      pickup.QuerySelector(".dispatch-paper__notes")!.TextContent
    );
    Assert.Equal(
      new[]
      {
        "Postal / country",
        "Commodity",
        "Weight",
        "Pieces",
        "Pallets",
        "Temperature",
      },
      pickup
        .QuerySelectorAll(".dispatch-paper__stop-facts dt")
        .Select(element => element.TextContent)
    );
    Assert.Equal(
      "Notes",
      pickup.QuerySelector(".dispatch-paper__stop-notes h3")!.TextContent
    );
    Assert.Empty(
      pickup.QuerySelectorAll(
        ".dispatch-paper__stop-facts .dispatch-paper__notes"
      )
    );
    Assert.Single(pickup.QuerySelectorAll(".dispatch-load__street"));
    foreach (
      var text in new[]
      {
        "54777",
        "Historic Driver",
        "ARCHIVE-1",
        "ORDER-23",
        "REF-17",
        "123 Warehouse Road",
        "Apples",
        "42,000",
        "120",
        "20",
        "29715",
        "US",
        "34",
        "Keep refrigerated",
        "17.23 CAD",
        "4.56 CAD",
      }
    )
      Assert.Contains(text, component.Markup);
    Assert.DoesNotContain("Live Driver", component.Markup);
    component.Render(parameters => parameters.Add(dialog => dialog.Load, load));
    Assert.Contains("is-stop-view", component.Find("dialog").ClassName);
    await component
      .Find(".dispatch-load-dialog__back")
      .ClickAsync(new MouseEventArgs());
    Assert.DoesNotContain("is-stop-view", component.Find("dialog").ClassName);
    Assert.Equal(
      2,
      component.FindAll(".dispatch-paper__stops > li:not([hidden])").Count
    );
    Assert.Single(
      module.Invocations,
      invocation => invocation.Identifier == "show"
    );
    await component
      .Find("button[aria-label='Close load details']")
      .ClickAsync(new MouseEventArgs());
    await component
      .Find("dialog")
      .TriggerEventAsync("onclose", EventArgs.Empty);
    Assert.Equal(1, closed);
    Assert.Single(
      module.Invocations,
      invocation => invocation.Identifier == "close"
    );
  }

  [Fact]
  public void CompletedOverrideNeverDisplaysLiveEtaOrCurrentPhase()
  {
    using var context = new BunitContext();
    context.Services.AddSingleton(TimeProvider.System);
    context.JSInterop.SetupModule("./js/generated/shared/loadDialog.js").Mode =
      JSRuntimeMode.Loose;
    var load = DispatchFinancialViewsTests.Load();
    foreach (var stop in load.Stops)
      stop.PickedUpAt = null;
    var component = context.Render<DispatchLoadDialog>(parameters =>
      parameters
        .Add(dialog => dialog.Load, load)
        .Add(dialog => dialog.Completed, true)
        .Add(dialog => dialog.Phase, "Current")
    );
    Assert.Equal(
      "Completed",
      component.Find(".dispatch-paper__tab-status").TextContent
    );
    Assert.DoesNotContain(
      "Current",
      component.Find(".dispatch-paper__identity").TextContent
    );
    Assert.Equal(2, component.FindAll(".dispatch-load__completed").Count);
    Assert.Empty(
      component.FindAll(
        ".dispatch-cycle-forecast, .dispatch-load__eta-missing, .stop-hours"
      )
    );
  }

  [Theory]
  [InlineData("Driver start", "No truck", true)]
  [InlineData("Collect truck", "Bobtail", false)]
  [InlineData("Collect trailer", "Empty", false)]
  public void NonCargoStopHidesImportedCargoAndEmptyDisclosureButPreservesUsefulNotes(
    string job,
    string state,
    bool driverOnly
  )
  {
    using var context = new BunitContext();
    context.Services.AddSingleton(TimeProvider.System);
    context.JSInterop.SetupModule("./js/generated/shared/loadDialog.js").Mode =
      JSRuntimeMode.Loose;
    var load = DispatchFinancialViewsTests.Load();
    var stop = load.Stops[0];
    stop.Job = job;
    stop.StateAfter = state;
    stop.DriverOnly = driverOnly;
    stop.Commodity = "Food Products";
    stop.Weight = 43063;
    stop.Pallets = 26;
    stop.Pieces = 15;
    stop.Temperature = "34";
    var component = context.Render<DispatchLoadDialog>(parameters =>
      parameters.Add(dialog => dialog.Load, load)
    );
    var selector = $"[data-stop-id='{stop.Id}']";
    Assert.Empty(component.Find(selector).QuerySelectorAll("details"));
    Assert.DoesNotContain(
      "Food Products",
      component.Find(selector).TextContent
    );

    stop.Notes = "Meet the driver at the office.";
    component.Render(parameters => parameters.Add(dialog => dialog.Load, load));
    Assert.Single(
      component.Find(selector).QuerySelectorAll(".dispatch-load__more-details")
    );
    Assert.Contains(stop.Notes, component.Find(selector).TextContent);
    Assert.Empty(
      component
        .Find(selector)
        .QuerySelectorAll(".dispatch-paper__stop-facts > div")
    );
    Assert.Equal("Food Products", stop.Commodity);
    Assert.Equal(43063m, stop.Weight);
    Assert.Equal(26m, stop.Pallets);
  }
}
