using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Channels;
using Bunit;
using Client.Models.DTO.Planning;
using Client.Pages.FleetMap;
using Client.Services;
using Client.Tests.Support;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using Microsoft.JSInterop;

namespace Client.Tests.Fleet;

[Trait("Category", "Fleet")]
[Trait("Kind", "Component")]
public sealed class FleetMapComponentTests
{
  [Theory]
  [InlineData(false)]
  [InlineData(true)]
  public async Task FuelSaveOrResetSupersedesPendingPollAndAllCachedDispatchAliases(bool reset)
  {
    using var fixture = new SelectionFixture();
    var original = fixture.Plan(fixture.TruckA);
    original.State!.Plan!.FuelPlan = new() { TruckId = fixture.TruckA, PurchaseGallons = 20, CalculatedAt = DateTime.UtcNow };
    fixture.StoreDispatchAlias(original);
    var stale = JsonSerializer.Deserialize<AutomaticPlanningResult>(JsonSerializer.Serialize(original))!;
    var updated = JsonSerializer.Deserialize<AutomaticPlanningResult>(JsonSerializer.Serialize(original))!;
    updated.State!.Plan!.FuelPlan!.PurchaseGallons = 80;
    updated.State.Plan.FuelPlan.ManuallyEdited = !reset;
    var component = fixture.Render();
    component.WaitForAssertion(() => Assert.Contains(fixture.Js.Calls, call => call.Name == "setTrucks"));
    await component.InvokeAsync(() => component.Instance.OnTruckSelected(fixture.TruckA.ToString()));
    await component.Find("[aria-label='Edit fuel plan']").ClickAsync(new MouseEventArgs());
    component.WaitForAssertion(() => Assert.Single(component.FindAll(".fuel-plan-editor")));
    Assert.Equal(fixture.TruckA.ToString(), fixture.Js.Calls.Last(call => call.Name == "setFuelEditorTruck").Args![0]);
    Assert.False(component.Find(".fleet-map-page__background").HasAttribute("inert"));
    Assert.False(component.Find(".fleet-map-page__background").HasAttribute("aria-hidden"));
    Assert.Single(component.FindAll("#fleet-map"));
    Assert.Empty(component.FindAll(".fleet-map-stage--fuel-editor, .fuel-plan-editor__map-slot"));
    Assert.Null(component.Find("#fleet-map").Closest("[inert]"));
    var editor = component.FindComponent<Client.Shared.Fuel.FuelPlanEditor.FuelPlanEditor>();
    fixture.DeferPlanning = true;
    await component.InvokeAsync(() => fixture.Clock.Advance(TimeSpan.FromSeconds(10)));
    var olderRead = await fixture.ReadPlanningAsync();
    if (reset)
      await component.InvokeAsync(() => editor.Instance.Reset.InvokeAsync(updated));
    else
    {
      var saved = component.InvokeAsync(() => editor.Instance.Saved.InvokeAsync(new FuelPlanEditPreview(
        updated.State.Plan.FuelPlan, [], original.State.Plan.FuelPlan.CalculatedAt, 200, 200, [])));
      var forcedRead = await fixture.ReadPlanningAsync();
      forcedRead.Reply(updated);
      await saved;
    }
    olderRead.Reply(stale);
    component.WaitForAssertion(() =>
    {
      Assert.Empty(component.FindAll(".fuel-plan-editor"));
      Assert.Null(fixture.Js.Calls.Last(call => call.Name == "setFuelEditorTruck").Args![0]);
      using var payload = fixture.LastCurrentPayload();
      Assert.Equal(80, payload.RootElement.GetProperty("fuelPlan").GetProperty("purchaseGallons").GetDouble());
      Assert.Equal(80, fixture.CachedPlan(fixture.TruckA)!.State!.Plan!.FuelPlan!.PurchaseGallons);
      Assert.Equal(80, fixture.CachedDispatchPlan(original.DispatchId!.Value)!.State!.Plan!.FuelPlan!.PurchaseGallons);
    });
  }

  [Fact]
  public async Task FuelEditorRejectsOldSelectionCallbacksAndClosesWhenCurrentTruckChanges()
  {
    using var fixture = new SelectionFixture();
    var component = fixture.Render();
    component.WaitForAssertion(() => Assert.Contains(fixture.Js.Calls, call => call.Name == "setTrucks"));
    await component.InvokeAsync(() => component.Instance.OnTruckSelected(fixture.TruckA.ToString()));
    var station = Guid.NewGuid().ToString();
    await component.InvokeAsync(() => component.Instance.OnFuelStationEdit(fixture.TruckB.ToString(),
      fixture.Plan(fixture.TruckB).DispatchId!.Value.ToString(), station, "Wrong station", null, false));
    Assert.Empty(component.FindAll(".fuel-plan-editor"));
    await component.InvokeAsync(() => component.Find(".fleet-map-mobile-summary__toggle").ClickAsync(new MouseEventArgs()));
    Assert.True(component.Find(".fleet-map-info-reserved").ClassList.Contains("is-expanded"));
    await component.InvokeAsync(() => component.Instance.OnFuelStationEdit(fixture.TruckA.ToString(),
      fixture.Plan(fixture.TruckA).DispatchId!.Value.ToString(), station, "Selected station", null, false));
    component.WaitForAssertion(() => Assert.Single(component.FindAll(".fuel-plan-editor")));
    Assert.False(component.Find(".fleet-map-info-reserved").ClassList.Contains("is-expanded"));
    Assert.Contains(fixture.Js.Calls, call => call.Name == "closeStationPopup");
    Assert.True(component.Find("[aria-label='Calculate Fuel']").HasAttribute("disabled"));
    await component.InvokeAsync(() => component.Instance.OnTruckSelected(fixture.TruckB.ToString()));
    Assert.Empty(component.FindAll(".fuel-plan-editor"));
    Assert.False(component.Find(".fleet-map-page__background").HasAttribute("inert"));
    await component.InvokeAsync(() => component.Instance.OnFuelStationEdit(fixture.TruckA.ToString(),
      fixture.Plan(fixture.TruckA).DispatchId!.Value.ToString(), station, "Late station", null, false));
    Assert.Empty(component.FindAll(".fuel-plan-editor"));
  }

  [Fact]
  public async Task ColdRouteReadReservesTheSamePanelColumnsWithoutLifecycleMessages()
  {
    using var fixture = new SelectionFixture(cacheCurrentPlans: false) { DeferPreview = true, DeferPlanning = true };
    var component = fixture.Render();
    component.WaitForAssertion(() => Assert.Contains(fixture.Js.Calls, call => call.Name == "setTrucks"));
    var mapMarkup = component.Find("#fleet-map").OuterHtml;
    var selecting = component.InvokeAsync(() => component.Instance.OnTruckSelected(fixture.TruckA.ToString()));
    var preview = await fixture.ReadPreviewAsync();
    component.WaitForAssertion(() =>
    {
      var panel = component.Find("[aria-label='Current dispatch route']");
      Assert.Equal("true", panel.GetAttribute("aria-busy"));
      Assert.Equal(new[] { "Remaining", "Next Stop" },
        panel.QuerySelectorAll(".fleet-map-route-info__metric .fleet-map-route-info__label").Select(x => x.TextContent));
      Assert.Equal(2, panel.QuerySelectorAll(".fleet-map-route-info__metric").Length);
      Assert.NotNull(panel.QuerySelector(":scope > .fleet-map-route-info__load"));
      Assert.NotNull(panel.QuerySelector(".fleet-map-route-info__next"));
      Assert.NotNull(panel.QuerySelector(":scope > .fleet-map-route-info__appointment"));
      Assert.Contains("Total — mi · — km", panel.TextContent);
      Assert.NotNull(panel.QuerySelector(".arrival-estimate"));
      Assert.DoesNotContain("Loading saved route", component.Markup);
      Assert.Equal(mapMarkup, component.Find(".fleet-map-stage > #fleet-map").OuterHtml);
      Assert.Single(fixture.Js.Calls, call => call.Name == "createFleetMap");
      Assert.NotNull(panel.Closest(".fleet-map-stage > .fleet-map-info-reserved"));
    });

    preview.Reply(fixture.Plan(fixture.TruckA));
    var refresh = await fixture.ReadPlanningAsync();
    component.WaitForAssertion(() =>
    {
      var panel = component.Find("[aria-label='Current dispatch route']");
      Assert.False(panel.HasAttribute("aria-busy"));
      Assert.Equal(2, panel.QuerySelectorAll(".fleet-map-route-info__metric").Length);
      Assert.NotNull(panel.QuerySelector(":scope > .fleet-map-route-info__load"));
      Assert.NotNull(panel.QuerySelector(":scope > .fleet-map-route-info__appointment"));
      Assert.Contains(fixture.Plan(fixture.TruckA).State!.Plan!.OriginalPlannedMiles.ToString("N0"),
        panel.QuerySelector(".fleet-map-route-info__total")!.TextContent);
      Assert.DoesNotContain("Loading saved route", component.Markup);
      Assert.Equal(mapMarkup, component.Find(".fleet-map-stage > #fleet-map").OuterHtml);
      Assert.Single(fixture.Js.Calls, call => call.Name == "createFleetMap");
    });
    refresh.Reply(fixture.Plan(fixture.TruckA));
    await selecting;
  }

  [Theory]
  [InlineData(true)]
  [InlineData(false)]
  public async Task CurrentRouteAddressShowsNormalStreetThenBoldLocalityAndCopiesTheOriginal(bool hasJob)
  {
    using var fixture = new SelectionFixture();
    var plan = fixture.Plan(fixture.TruckA).State!.Plan!;
    var stop = new PlanStop(Guid.NewGuid(), "Mid 972", " 50 Patriot Dr, Middletown, DE 19709, US ", 1, new(40, -80))
      { Job = hasJob ? "Delivery" : "" };
    plan.Stops = [stop];
    plan.Tracking.NextStopId = stop.Id;
    plan.Tracking.NextStopLabel = "Delivery · Middletown, DE";
    var component = fixture.Render();
    component.WaitForAssertion(() => Assert.Contains(fixture.Js.Calls, call => call.Name == "setTrucks"));
    await component.InvokeAsync(() => component.Instance.OnTruckSelected(fixture.TruckA.ToString()));
    var next = component.Find("[aria-label='Current dispatch route'] .fleet-map-route-info__next");
    Assert.Equal("Delivery", next.QuerySelector(".fleet-map-route-info__label")!.TextContent);
    var address = next.QuerySelector("[title='Copy full address']")!;
    var lines = address.QuerySelector(".fleet-map-route-info__address-lines")!;
    Assert.Equal(new[] { "50 Patriot Dr", "Middletown, DE 19709, US" }, lines.Children.Select(line => line.TextContent));
    Assert.Equal("SPAN", lines.Children[0].TagName);
    Assert.Equal("STRONG", lines.Children[1].TagName);
    Assert.Empty(lines.Children[0].QuerySelectorAll("strong, b"));
    Assert.NotNull(address.QuerySelector(".fleet-map-route-info__address-icon > svg[aria-hidden='true']"));
    await component.InvokeAsync(() => component.Find("[title='Copy full address']").ClickAsync(new MouseEventArgs()));
    Assert.Contains(fixture.Js.Calls, call => call.Name == "navigator.clipboard.writeText" && Equals(call.Args![0], stop.Address));
  }

  [Theory]
  [InlineData(true)]
  [InlineData(false)]
  public async Task UnifiedTruckHeaderUsesOnlyTheCurrentDriverCycleAndClearsItOnSelection(bool available)
  {
    using var fixture = new SelectionFixture();
    var now = fixture.Clock.GetUtcNow();
    var plan = fixture.Plan(fixture.TruckA).State!.Plan!;
    var stop = new PlanStop(Guid.NewGuid(), "Delivery", "50 Patriot Dr, Middletown, DE 19709, US", 1, new(40, -80));
    plan.Stops = [stop];
    plan.Tracking.NextStopId = stop.Id;
    var current = new StopCycleForecast(300, now.AddHours(12), 185, "UTC", true);
    var future = new StopCycleForecast(700, now.AddDays(2), 660, "UTC", true);
    var eta = new DispatchEta(now.UtcDateTime, now.AddMinutes(2).UtcDateTime,
      [new(stop.Id, now.AddHours(1), "UTC", null, 0, 60, 0) { DispatchId = plan.DispatchId, CycleAfterDeparture = future }], null, [])
      { CycleAtCalculation = available ? current : null };
    fixture.SetEta(fixture.TruckA, eta);
    var component = fixture.Render();
    component.WaitForAssertion(() => Assert.Contains(fixture.Js.Calls, call => call.Name == "setTrucks"));
    await component.InvokeAsync(() => component.Instance.OnTruckSelected(fixture.TruckA.ToString()));
    component.WaitForAssertion(() =>
    {
      Assert.Contains("Truck 54777", component.Find(".fleet-map-truck-info__identity").TextContent);
      Assert.Equal(3, component.FindAll(".fleet-map-truck-info__telemetry > .fleet-map-truck-info__reading").Count);
      Assert.Single(component.FindAll(".fleet-map-truck-info__hours > .driver-hours-panel"));
      Assert.Equal("HOS (Samsara)", component.Find(".fleet-map-truck-info__hours-label").TextContent);
      Assert.Single(component.FindAll(".fleet-map-truck-info__reading--fuel > .fuel-reading"));
      Assert.Single(component.FindAll(".fleet-map-truck-info__reading--fuel > .fuel-reading--metric"));
      Assert.Single(component.FindAll(".fleet-map-truck-info__illustration > .truck-illustration"));
      Assert.Single(component.FindAll(".truck-illustration__trailer"));
      Assert.Equal(2, component.FindAll(".fleet-map-truck-info__reading > small > svg[aria-hidden='true']").Count);
      Assert.Single(component.FindAll(".fleet-map-truck-info__duty > .driver-duty"));
      Assert.True(component.FindComponent<Client.Shared.DriverStatus.DriverDutySummary.DriverDutySummary>().Instance.Compact);
      var recap = component.FindComponent<Client.Shared.DriverStatus.DriverNextRecap.DriverNextRecap>();
      Assert.Equal(available ? current : null, recap.Instance.Snapshot?.Cycle);
      Assert.DoesNotContain("11h 00m", recap.Markup);
      if (available) Assert.Contains("+3h 05m", recap.Markup);
      else Assert.Contains("—", recap.Markup);
    });
    await component.InvokeAsync(() => component.Instance.OnTruckSelected(fixture.TruckB.ToString()));
    component.WaitForAssertion(() =>
    {
      var recap = component.FindComponent<Client.Shared.DriverStatus.DriverNextRecap.DriverNextRecap>();
      Assert.Null(recap.Instance.Snapshot);
      Assert.Contains("—", recap.Markup);
    });
  }

  [Fact]
  public async Task SelectionChangesOverlayTheSameMountedMapWithoutAddingFlowPanels()
  {
    using var fixture = new SelectionFixture();
    var component = fixture.Render();
    component.WaitForAssertion(() => Assert.Contains(fixture.Js.Calls, call => call.Name == "setTrucks"));
    var mapMarkup = component.Find(".fleet-map-stage > #fleet-map").OuterHtml;
    Assert.Single(component.FindAll(".fleet-map-stage > .fleet-map-info-reserved"));
    Assert.Empty(component.FindAll(".fleet-map-page__background .fleet-map-info-reserved"));
    Assert.False(component.Find(".fleet-map-info-reserved").ClassList.Contains("has-selection"));
    Assert.Single(component.FindAll(".fleet-map-inspector__native[hidden]"));
    Assert.Empty(component.FindAll(".fleet-map-info-empty"));
    await component.InvokeAsync(() => component.Instance.OnTruckSelected(fixture.TruckA.ToString()));
    component.WaitForAssertion(() => Assert.True(component.Find(".fleet-map-info-reserved").ClassList.Contains("has-selection")));
    Assert.Equal(mapMarkup, component.Find(".fleet-map-stage > #fleet-map").OuterHtml);
    Assert.Single(fixture.Js.Calls, call => call.Name == "createFleetMap");
    await component.InvokeAsync(() => component.Instance.OnTruckSelected(fixture.TruckB.ToString()));
    Assert.Equal(mapMarkup, component.Find(".fleet-map-stage > #fleet-map").OuterHtml);
    await component.InvokeAsync(() => component.Find("[aria-label='Close map information']").ClickAsync(new MouseEventArgs()));
    Assert.False(component.Find(".fleet-map-info-reserved").ClassList.Contains("has-selection"));
    Assert.True(component.Find("#fleet-map-details").HasAttribute("hidden"));
    Assert.Single(component.FindAll(".fleet-map-truck-info"));
    Assert.Single(component.FindAll(".fleet-map-route-info"));
    Assert.Equal("clearMapInspection", fixture.Js.Calls.Last().Name);
    Assert.Equal(mapMarkup, component.Find(".fleet-map-stage > #fleet-map").OuterHtml);
    Assert.Single(fixture.Js.Calls, call => call.Name == "createFleetMap");
    await component.InvokeAsync(() => component.Instance.OnTruckSelected(fixture.TruckA.ToString()));
    Assert.True(component.Find(".fleet-map-info-reserved").ClassList.Contains("has-selection"));
    Assert.Equal(mapMarkup, component.Find(".fleet-map-stage > #fleet-map").OuterHtml);
    Assert.Single(fixture.Js.Calls, call => call.Name == "createFleetMap");
  }

  [Fact]
  public async Task InspectorSwitchesOnePersistentHostAndRejectsStaleOrForeignCallbacks()
  {
    using var fixture = new SelectionFixture();
    var component = fixture.Render();
    component.WaitForAssertion(() => Assert.Contains(fixture.Js.Calls, call => call.Name == "setTrucks"));
    await component.InvokeAsync(() => component.Instance.OnTruckSelected(fixture.TruckA.ToString()));
    var header = component.Find("#fleet-map-details").TextContent;
    var httpCalls = fixture.HttpCalls;
    var mapMarkup = component.Find("#fleet-map").OuterHtml;
    await component.InvokeAsync(() => component.Instance.OnMapInspectorChanged("fuel", fixture.TruckA.ToString(), 10));
    Assert.Equal("fuel", component.Find(".fleet-map-inspector").GetAttribute("data-inspector-mode"));
    Assert.False(component.Find(".fleet-map-inspector__native").HasAttribute("hidden"));
    Assert.True(component.Find("#fleet-map-details").HasAttribute("hidden"));
    Assert.Equal(header, component.Find("#fleet-map-details").TextContent);
    await component.InvokeAsync(() => component.Instance.OnMapInspectorChanged("stop", fixture.TruckA.ToString(), 9));
    await component.InvokeAsync(() => component.Instance.OnMapInspectorChanged("stop", fixture.TruckB.ToString(), 11));
    await component.InvokeAsync(() => component.Instance.OnMapInspectorChanged("stop", null, 12));
    Assert.Equal("fuel", component.Find(".fleet-map-inspector").GetAttribute("data-inspector-mode"));
    await component.InvokeAsync(() => component.Instance.OnMapInspectorChanged("stop", fixture.TruckA.ToString(), 13));
    Assert.Equal("stop", component.Find(".fleet-map-inspector").GetAttribute("data-inspector-mode"));
    Assert.Single(component.FindAll(".fleet-map-inspector__native"));
    await component.InvokeAsync(() => component.Find(".fleet-map-inspector__back").ClickAsync(new MouseEventArgs()));
    Assert.Equal("truck", component.Find(".fleet-map-inspector").GetAttribute("data-inspector-mode"));
    Assert.False(component.Find("#fleet-map-details").HasAttribute("hidden"));
    Assert.True(component.Find(".fleet-map-inspector__native").HasAttribute("hidden"));
    Assert.Equal(header, component.Find("#fleet-map-details").TextContent);
    Assert.Equal(httpCalls + 1, fixture.HttpCalls);
    Assert.Single(fixture.Js.Calls, call => call.Name == "setStations");
    Assert.Equal(mapMarkup, component.Find("#fleet-map").OuterHtml);
    Assert.Single(fixture.Js.Calls, call => call.Name == "createFleetMap");
    await component.InvokeAsync(() => component.Instance.OnTruckSelected(fixture.TruckB.ToString()));
    await component.InvokeAsync(() => component.Instance.OnMapInspectorChanged("fuel", fixture.TruckA.ToString(), 14));
    Assert.Equal("truck", component.Find(".fleet-map-inspector").GetAttribute("data-inspector-mode"));
    await component.InvokeAsync(() => component.Instance.OnMapInspectorChanged("fuel", fixture.TruckB.ToString(), 15));
    Assert.Equal("fuel", component.Find(".fleet-map-inspector").GetAttribute("data-inspector-mode"));
  }

  [Fact]
  public async Task StationOnlyInspectionNeedsNoTruckOrPlanningRequest()
  {
    using var fixture = new SelectionFixture();
    var component = fixture.Render();
    component.WaitForAssertion(() => Assert.Contains(fixture.Js.Calls, call => call.Name == "setTrucks"));
    var httpCalls = fixture.HttpCalls;
    await component.InvokeAsync(() => component.Instance.OnMapInspectorChanged("fuel", null, 1));
    Assert.True(component.Find(".fleet-map-inspector").ClassList.Contains("has-selection"));
    Assert.False(component.Find(".fleet-map-inspector__native").HasAttribute("hidden"));
    Assert.Empty(component.FindAll(".fleet-map-inspector__back, #fleet-map-details"));
    await component.InvokeAsync(() => component.Find(".fleet-map-inspector").KeyDownAsync(new KeyboardEventArgs { Key = "Escape" }));
    Assert.False(component.Find(".fleet-map-inspector").ClassList.Contains("has-selection"));
    Assert.Equal(httpCalls + 1, fixture.HttpCalls);
    Assert.Single(fixture.Js.Calls, call => call.Name == "setStations");
    Assert.Equal("clearMapInspection", fixture.Js.Calls.Last().Name);
  }

  [Fact]
  public async Task DispatchOnlySelectionAcceptsOnlyTheAuthoritativePlansTruckForInspection()
  {
    using var fixture = new SelectionFixture();
    var plan = fixture.Plan(fixture.TruckA);
    fixture.StoreDispatchAlias(plan);
    var component = fixture.Render(dispatchId: plan.DispatchId);
    component.WaitForAssertion(() => Assert.Single(component.FindAll(".fleet-map-route-info")));
    await fixture.Js.ReadCurrentPayloadAsync();
    await component.InvokeAsync(() => component.Instance.OnMapInspectorChanged("stop", fixture.TruckA.ToString(), 1));
    Assert.Equal("stop", component.Find(".fleet-map-inspector").GetAttribute("data-inspector-mode"));
    await component.InvokeAsync(() => component.Instance.OnMapInspectorChanged("fuel", fixture.TruckB.ToString(), 2));
    Assert.Equal("stop", component.Find(".fleet-map-inspector").GetAttribute("data-inspector-mode"));
    await component.InvokeAsync(() => component.Find(".fleet-map-inspector__back").ClickAsync(new MouseEventArgs()));
    var transition = fixture.Js.Calls.Last(call => call.Name == "setInspectorMode");
    Assert.Equal(fixture.TruckA.ToString(), transition.Args![1]);
    Assert.Equal("truck", component.Find(".fleet-map-inspector").GetAttribute("data-inspector-mode"));
    using var payload = fixture.LastCurrentPayload();
    Assert.Equal(plan.DispatchId, payload.RootElement.GetProperty("dispatchId").GetGuid());
  }

  [Fact]
  public void StartupQueryTargetReachesTheCameraBeforeTheFirstFleetSnapshot()
  {
    using var fixture = new Fixture();
    var component = fixture.Render(fixture.TruckId);
    component.WaitForAssertion(() => Assert.Contains(fixture.Js.Calls, call => call.Name == "focusTruck"));
    var calls = fixture.Js.Calls.ToArray();
    var options = Array.FindIndex(calls, call => call.Name == "setOptions");
    var snapshot = Array.FindIndex(calls, call => call.Name == "setTrucks");
    Assert.True(options >= 0 && snapshot > options);
    Assert.Equal(fixture.TruckId, JsonSerializer.SerializeToElement(calls[options].Args![0])
      .GetProperty("initialTruckId").GetGuid());
    var focus = calls.First(call => call.Name == "focusTruck");
    Assert.Equal(true, focus.Args![2]);
  }

  [Fact]
  public void FailedInitialLocationsRevealTheMapInsteadOfLeavingTheStartupHostHidden()
  {
    using var fixture = new Fixture { FailLocations = true };
    var component = fixture.Render();
    component.WaitForAssertion(() => Assert.Contains(fixture.Js.Calls, call => call.Name == "finishInitialView"));
    Assert.DoesNotContain(fixture.Js.Calls, call => call.Name == "setTrucks");
  }

  [Theory]
  [InlineData(false)]
  [InlineData(true)]
  public async Task FailedFuelCalculationRetainsTheValidPlanUntilNormalReadRevalidation(bool invalidated)
  {
    using var fixture = new SelectionFixture();
    var original = fixture.Plan(fixture.TruckA);
    original.State!.Plan!.FuelPlan = new()
    {
      TruckId = fixture.TruckA,
      ScheduleImpact = new(DateTime.UtcNow, true, true, false, false, 5, 0, [], null),
      Stops = [new() { StationId = Guid.NewGuid(), VisitKey = "saved-visit", Name = "Saved fuel station", BuyGallons = 25,
        ArrivalGallons = 18, MilesAhead = 100 }]
    };
    var component = fixture.Render();
    component.WaitForAssertion(() => Assert.Contains(fixture.Js.Calls, x => x.Name == "setTrucks"));
    await component.InvokeAsync(() => component.Instance.OnTruckSelected(fixture.TruckA.ToString()));
    Assert.Empty(component.FindAll(".fuel-plan-summary"));
    using (var payload = fixture.LastCurrentPayload())
      Assert.Equal(25, payload.RootElement.GetProperty("fuelPlan").GetProperty("stops")[0].GetProperty("buyGallons").GetDouble());
    fixture.DeferPlanning = true;
    var calculating = component.Find("button[aria-label='Calculate Fuel']").ClickAsync(new MouseEventArgs());
    var revalidation = await fixture.ReadPlanningAsync();
    Assert.Empty(component.FindAll(".fuel-plan-summary"));
    using (var payload = fixture.LastCurrentPayload())
      Assert.Equal(25, payload.RootElement.GetProperty("fuelPlan").GetProperty("stops")[0].GetProperty("buyGallons").GetDouble());
    Assert.Single(component.FindAll("[aria-label='Current dispatch route']"));
    Assert.NotNull(fixture.CachedPlan(fixture.TruckB));
    var refreshed = JsonSerializer.Deserialize<AutomaticPlanningResult>(JsonSerializer.Serialize(original))!;
    if (invalidated)
    {
      refreshed.State!.Plan!.FuelPlan!.NeedsRefresh = true;
      refreshed.State.Plan.FuelPlan.RefreshReasons = ["Assignments changed. Recalculate fuel."];
    }
    revalidation.Reply(refreshed);
    await calculating;
    Assert.Empty(component.FindAll(".fuel-plan-summary"));
    Assert.DoesNotContain("Assignments changed. Recalculate fuel.", component.Markup);
    Assert.DoesNotContain("Buy 25 gal", component.Markup);
    using (var payload = fixture.LastCurrentPayload())
    {
      var fuel = payload.RootElement.GetProperty("fuelPlan");
      Assert.Equal(invalidated, fuel.GetProperty("needsRefresh").GetBoolean());
      Assert.Equal(25, fuel.GetProperty("stops")[0].GetProperty("buyGallons").GetDouble());
    }
    Assert.Single(component.FindAll("[aria-label='Current dispatch route']"));
    Assert.NotNull(fixture.CachedPlan(fixture.TruckB));
  }

  [Fact]
  public async Task StationToggleReusesLoadedDateAndIftaDoesNotRequestStationsAgain()
  {
    using var fixture = new Fixture();
    var component = fixture.Render();
    component.WaitForAssertion(() => Assert.Contains(fixture.Js.Calls, x => x.Name == "setTrucks"));
    Assert.Empty(component.FindAll(".fleet-map-key__fuel"));
    await component.InvokeAsync(() => Toggle(component, "Fuel Stations").ChangeAsync(new ChangeEventArgs { Value = true }));
    Assert.Equal(1, fixture.StationCalls);
    Assert.Single(fixture.Js.Calls, x => x.Name == "setStations");
    Assert.Equal("Fuel · Your price", component.Find(".fleet-map-key__fuel-label").TextContent);
    Assert.Equal("Within each currency", component.Find(".fleet-map-key__note").TextContent);
    Assert.Equal("No price", component.Find(".fleet-map-key__missing").TextContent);
    await component.InvokeAsync(() => Toggle(component, "Fuel Stations").ChangeAsync(new ChangeEventArgs { Value = false }));
    Assert.Empty(component.FindAll(".fleet-map-key__fuel"));
    await component.InvokeAsync(() => Toggle(component, "Fuel Stations").ChangeAsync(new ChangeEventArgs { Value = true }));
    await component.InvokeAsync(() => Toggle(component, "IFTA").ChangeAsync(new ChangeEventArgs { Value = true }));
    Assert.Equal("Fuel · After IFTA", component.Find(".fleet-map-key__fuel-label").TextContent);
    Assert.Equal(1, fixture.StationCalls);
    Assert.Single(fixture.Js.Calls, x => x.Name == "setStations");
    Assert.Equal(true, fixture.Js.Calls.Last(x => x.Name == "setIfta").Args![0]);
  }

  [Fact]
  public async Task OpeningFuelInspectionLoadsQuotesWithoutEnablingStationMarkers()
  {
    using var fixture = new Fixture();
    var component = fixture.Render();
    component.WaitForAssertion(() => Assert.Contains(fixture.Js.Calls, x => x.Name == "setTrucks"));
    await component.InvokeAsync(() => component.Instance.OnMapInspectorChanged("fuel", null, 1));
    Assert.Equal(1, fixture.StationCalls);
    Assert.False(Toggle(component, "Fuel Stations").HasAttribute("checked"));
    Assert.Empty(component.FindAll(".fleet-map-key__fuel"));
    Assert.Single(fixture.Js.Calls, x => x.Name == "setStations");
    await component.InvokeAsync(() => component.Instance.OnMapInspectorChanged("closed", null, 2));
    await component.InvokeAsync(() => component.Instance.OnMapInspectorChanged("fuel", null, 3));
    Assert.Equal(1, fixture.StationCalls);
  }

  [Fact]
  public async Task SearchingAfterHidingTrucksRestoresBothVisibilityAndFocus()
  {
    using var fixture = new Fixture();
    var component = fixture.Render();
    component.WaitForAssertion(() => Assert.Contains(fixture.Js.Calls, x => x.Name == "setTrucks"));
    Toggle(component, "Trucks").Change(false);
    Assert.Equal(false, fixture.Js.Calls.Last(x => x.Name == "setTrucksVisible").Args![0]);
    await component.Find("#fleet-truck-search").InputAsync(new ChangeEventArgs { Value = "54777" });
    Assert.True(Toggle(component, "Trucks").HasAttribute("checked"));
    Assert.Equal(true, fixture.Js.Calls.Last(x => x.Name == "setTrucksVisible").Args![0]);
    Assert.Contains(fixture.Js.Calls, x => x.Name == "focusTruck" && Equals(x.Args![0], fixture.TruckId.ToString()));
  }

  [Fact]
  public async Task SuccessfulUnchangedPollClearsAnErrorWithoutResendingNextLoadGeometry()
  {
    using var fixture = new Fixture();
    var component = fixture.Render();
    component.WaitForAssertion(() => Assert.Contains(fixture.Js.Calls, x => x.Name == "setTrucks"));
    await component.InvokeAsync(() => component.Instance.OnTruckSelected(fixture.TruckId.ToString()));
    await Toggle(component, "Next loads").ChangeAsync(new ChangeEventArgs { Value = true });
    Assert.Single(fixture.Js.Calls, x => x.Name == "setNextLoadsBytes");
    await component.InvokeAsync(() => fixture.Clock.Advance(TimeSpan.FromSeconds(10)));
    component.WaitForAssertion(() => Assert.Contains("Next load routes could not be loaded.", component.Markup));
    await component.InvokeAsync(() => fixture.Clock.Advance(TimeSpan.FromSeconds(10)));
    component.WaitForAssertion(() => Assert.DoesNotContain("Next load routes could not be loaded.", component.Markup));
    Assert.Equal(3, fixture.NextCalls);
    Assert.Single(fixture.Js.Calls, x => x.Name == "setNextLoadsBytes");
  }

  [Fact]
  public async Task OnOffOnRejectsTheEarlierResponseAndReusesTheLatestVisibleRoutes()
  {
    using var fixture = new SelectionFixture();
    var component = fixture.Render();
    component.WaitForAssertion(() => Assert.Contains(fixture.Js.Calls, x => x.Name == "setTrucks"));
    await component.InvokeAsync(() => component.Instance.OnTruckSelected(fixture.TruckA.ToString()));

    var firstToggle = Toggle(component, "Next loads").ChangeAsync(new ChangeEventArgs { Value = true });
    var first = await fixture.ReadNextAsync();
    await Toggle(component, "Next loads").ChangeAsync(new ChangeEventArgs { Value = false });
    Assert.True(first.Cancellation.IsCancellationRequested);
    var secondToggle = Toggle(component, "Next loads").ChangeAsync(new ChangeEventArgs { Value = true });
    var second = await fixture.ReadNextAsync();
    fixture.AssertIdentity(second, fixture.TruckA, "");
    var latest = fixture.FutureLoad(202);
    second.Reply("latest", [fixture.CurrentLoad(fixture.TruckA), latest]);
    await secondToggle;
    fixture.AssertOnlyFuturePublished(latest.Id);

    first.Reply("stale", [fixture.FutureLoad(101)]);
    await firstToggle;
    fixture.AssertOnlyFuturePublished(latest.Id);
    Assert.True(Toggle(component, "Next loads").HasAttribute("checked"));
    Assert.DoesNotContain("Next load routes could not be loaded.", component.Markup);
    Assert.Equal(new[] { true, false, true }, fixture.Js.Calls
      .Where(x => x.Name == "setNextLoadsVisible").TakeLast(3).Select(x => (bool)x.Args![0]!));

    var clears = fixture.Js.Calls.Count(x => x.Name == "clearNextLoads");
    await Toggle(component, "Next loads").ChangeAsync(new ChangeEventArgs { Value = false });
    var reopen = Toggle(component, "Next loads").ChangeAsync(new ChangeEventArgs { Value = true });
    var unchanged = await fixture.ReadNextAsync();
    fixture.AssertIdentity(unchanged, fixture.TruckA, "latest");
    unchanged.ReplyUnchanged("latest");
    await reopen;
    fixture.AssertOnlyFuturePublished(latest.Id);
    Assert.Equal(clears, fixture.Js.Calls.Count(x => x.Name == "clearNextLoads"));
  }

  [Fact]
  public async Task ReturningToTruckAIgnoresBothEarlierAAndBResponsesAndKeepsTheNewRevision()
  {
    using var fixture = new SelectionFixture();
    var component = fixture.Render();
    component.WaitForAssertion(() => Assert.Contains(fixture.Js.Calls, x => x.Name == "setTrucks"));
    await component.InvokeAsync(() => component.Instance.OnTruckSelected(fixture.TruckA.ToString()));
    var initialToggle = Toggle(component, "Next loads").ChangeAsync(new ChangeEventArgs { Value = true });
    var oldA = await fixture.ReadNextAsync();
    fixture.AssertIdentity(oldA, fixture.TruckA, "");

    var selectB = component.InvokeAsync(() => component.Instance.OnTruckSelected(fixture.TruckB.ToString()));
    var oldB = await fixture.ReadNextAsync();
    fixture.AssertIdentity(oldB, fixture.TruckB, "");
    Assert.True(oldA.Cancellation.IsCancellationRequested);

    var selectA = component.InvokeAsync(() => component.Instance.OnTruckSelected(fixture.TruckA.ToString()));
    var currentA = await fixture.ReadNextAsync();
    fixture.AssertIdentity(currentA, fixture.TruckA, "");
    Assert.True(oldB.Cancellation.IsCancellationRequested);
    var latest = fixture.FutureLoad(303);
    currentA.Reply("current-a", [fixture.CurrentLoad(fixture.TruckA), latest]);
    await selectA;
    fixture.AssertOnlyFuturePublished(latest.Id);

    oldB.Reply("stale-b", [fixture.FutureLoad(202)]);
    await selectB;
    oldA.Reply("stale-a", [fixture.FutureLoad(101)]);
    await initialToggle;
    fixture.AssertOnlyFuturePublished(latest.Id);
    Assert.Contains("54777", component.Find(".fleet-map-truck-info").TextContent);
    Assert.DoesNotContain("Next load routes could not be loaded.", component.Markup);
    using var route = JsonDocument.Parse((byte[])fixture.Js.Calls.Last(x => x.Name == "setRouteBytes").Args![0]!);
    Assert.Equal(fixture.TruckA, route.RootElement.GetProperty("truckId").GetGuid());

    await component.InvokeAsync(() => fixture.Clock.Advance(TimeSpan.FromSeconds(10)));
    var poll = await fixture.ReadNextAsync();
    fixture.AssertIdentity(poll, fixture.TruckA, "current-a");
    var renders = component.RenderCount;
    poll.ReplyUnchanged("current-a");
    component.WaitForAssertion(() => Assert.True(component.RenderCount > renders));
    Assert.DoesNotContain("Next load routes could not be loaded.", component.Markup);
    fixture.AssertOnlyFuturePublished(latest.Id);
  }

  [Fact]
  public async Task EnabledNextLoadsStartAndDisplayWhileCurrentPlanningAndDetailsAreStillPending()
  {
    using var fixture = new SelectionFixture { DeferPlanning = true, DeferDetails = true };
    var component = fixture.Render();
    component.WaitForAssertion(() => Assert.Contains(fixture.Js.Calls, x => x.Name == "setTrucks"));
    await Toggle(component, "Next loads").ChangeAsync(new ChangeEventArgs { Value = true });

    var selection = component.InvokeAsync(() => component.Instance.OnTruckSelected(fixture.TruckA.ToString()));
    var next = await fixture.ReadNextAsync();
    var planning = await fixture.ReadPlanningAsync();
    var details = await fixture.ReadDetailsAsync();
    fixture.AssertIdentity(next, fixture.TruckA, "");
    var future = fixture.FutureLoad(202);
    next.Reply("ready", [future]);
    await fixture.Js.ReadNextPayloadAsync();
    fixture.AssertOnlyFuturePublished(future.Id);
    Assert.False(selection.IsCompleted);
    Assert.False(planning.Response.Task.IsCompleted);
    Assert.False(details.Response.Task.IsCompleted);

    planning.Reply(fixture.Plan(fixture.TruckA));
    details.Reply(new { id = fixture.Plan(fixture.TruckA).DispatchId, loadNumber = 1358 });
    await selection;
    Assert.Equal(1, fixture.NextCalls);
    Assert.Equal(0, fixture.PreviewCalls);
  }

  [Theory]
  [InlineData(false)]
  [InlineData(true)]
  public async Task CurrentPopupLoadReferenceArrivesIndependentlyOfRouteGeometry(bool detailsFirst)
  {
    using var fixture = new SelectionFixture(cacheCurrentPlans: false)
      { DeferPreview = true, DeferPlanning = true, DeferDetails = true };
    var component = fixture.Render();
    component.WaitForAssertion(() => Assert.Contains(fixture.Js.Calls, x => x.Name == "setTrucks"));
    var selection = component.InvokeAsync(() => component.Instance.OnTruckSelected(fixture.TruckA.ToString()));
    var saved = fixture.Plan(fixture.TruckA);
    (await fixture.ReadPreviewAsync()).Reply(saved with { State = null });
    var planning = await fixture.ReadPlanningAsync();
    var details = await fixture.ReadDetailsAsync();
    Assert.Equal($"/api/dispatch/{saved.DispatchId}", details.Uri.AbsolutePath);
    var httpCalls = fixture.HttpCalls;
    var routeCalls = fixture.Js.Calls.Count(x => x.Name == "setRouteBytes");
    var etaCalls = fixture.Js.Calls.Count(x => x.Name == "setStopEtas");

    if (detailsFirst)
    {
      details.Reply(new { id = saved.DispatchId, loadNumber = 1373, orderNumber = "566126837" });
      component.WaitForAssertion(() => Assert.Equal("566126837", component.Find("[title='Copy order number']").TextContent));
      Assert.Equal(JsonValueKind.Null, fixture.LastLoadReference().ValueKind);
      Assert.Equal(routeCalls, fixture.Js.Calls.Count(x => x.Name == "setRouteBytes"));
      Assert.False(planning.Response.Task.IsCompleted);
      planning.Reply(saved);
    }
    else
    {
      planning.Reply(saved);
      component.WaitForAssertion(() =>
      {
        using var current = fixture.LastCurrentPayload();
        Assert.Equal(saved.DispatchId, current.RootElement.GetProperty("dispatchId").GetGuid());
        Assert.Equal(routeCalls + 1, fixture.Js.Calls.Count(x => x.Name == "setRouteBytes"));
        Assert.Equal(etaCalls + 1, fixture.Js.Calls.Count(x => x.Name == "setStopEtas"));
      });
      Assert.Equal(JsonValueKind.Null, fixture.LastLoadReference().ValueKind);
      Assert.False(details.Response.Task.IsCompleted);
      details.Reply(new { id = saved.DispatchId, loadNumber = 1373, orderNumber = "566126837" });
    }

    await selection;
    fixture.AssertLoadReference(saved.DispatchId!.Value, 1373, "566126837");
    Assert.Single(fixture.Js.Calls, x => x.Name == "setLoadReference" && x.Args![0] is not null);
    Assert.Equal(routeCalls + 1, fixture.Js.Calls.Count(x => x.Name == "setRouteBytes"));
    Assert.Equal(httpCalls, fixture.HttpCalls);
    Assert.Equal(1, fixture.PreviewCalls);
    Assert.Equal(1, fixture.PlanningCalls);
    Assert.Equal(1, fixture.DetailsCalls);
    Assert.Equal(0, fixture.NextCalls);
  }

  [Fact]
  public async Task LateCurrentDetailsCannotOverwriteTheSelectedTrucksPopupLoadReference()
  {
    using var fixture = new SelectionFixture { DeferDetails = true };
    var component = fixture.Render();
    component.WaitForAssertion(() => Assert.Contains(fixture.Js.Calls, x => x.Name == "setTrucks"));
    var selectA = component.InvokeAsync(() => component.Instance.OnTruckSelected(fixture.TruckA.ToString()));
    var detailsA = await fixture.ReadDetailsAsync();
    Assert.Equal($"/api/dispatch/{fixture.Plan(fixture.TruckA).DispatchId}", detailsA.Uri.AbsolutePath);
    var selectB = component.InvokeAsync(() => component.Instance.OnTruckSelected(fixture.TruckB.ToString()));
    var detailsB = await fixture.ReadDetailsAsync();
    var current = fixture.Plan(fixture.TruckB).DispatchId!.Value;
    Assert.Equal($"/api/dispatch/{current}", detailsB.Uri.AbsolutePath);
    detailsB.Reply(new { id = current, loadNumber = 1373, orderNumber = "566126837" });
    await selectB;
    fixture.AssertLoadReference(current, 1373, "566126837");
    var references = fixture.Js.Calls.Count(x => x.Name == "setLoadReference");
    var routes = fixture.Js.Calls.Count(x => x.Name == "setRouteBytes");
    var httpCalls = fixture.HttpCalls;

    detailsA.Reply(new { id = fixture.Plan(fixture.TruckA).DispatchId, loadNumber = 1358, orderNumber = "565606595" });
    await selectA;
    fixture.AssertLoadReference(current, 1373, "566126837");
    Assert.Equal(references, fixture.Js.Calls.Count(x => x.Name == "setLoadReference"));
    Assert.Equal(routes, fixture.Js.Calls.Count(x => x.Name == "setRouteBytes"));
    Assert.Equal(httpCalls, fixture.HttpCalls);
    Assert.All(fixture.Js.Calls.Where(x => x.Name == "setLoadReference" && x.Args![0] is not null), call =>
      Assert.Equal(current, JsonSerializer.SerializeToElement(call.Args![0], new JsonSerializerOptions(JsonSerializerDefaults.Web))
        .GetProperty("dispatchId").GetGuid()));
    Assert.Contains("64888", component.Find(".fleet-map-truck-info").TextContent);
    Assert.Equal("1373", component.Find("[title='Copy load number']").TextContent);
    Assert.Equal("566126837", component.Find("[title='Copy order number']").TextContent);
    Assert.DoesNotContain("565606595", component.Markup);
    using var route = fixture.LastCurrentPayload();
    Assert.Equal(fixture.TruckB, route.RootElement.GetProperty("truckId").GetGuid());
  }

  [Fact]
  public async Task ColdSelectionWithUnavailablePreviewUsesTheResolvedCurrentIdentityWithoutASecondNextLoadRequest()
  {
    using var fixture = new SelectionFixture(cacheCurrentPlans: false) { DeferPlanning = true, PreviewUnavailable = true };
    var component = fixture.Render();
    component.WaitForAssertion(() => Assert.Contains(fixture.Js.Calls, x => x.Name == "setTrucks"));
    await Toggle(component, "Next loads").ChangeAsync(new ChangeEventArgs { Value = true });
    var selection = component.InvokeAsync(() => component.Instance.OnTruckSelected(fixture.TruckA.ToString()));
    var planning = await fixture.ReadPlanningAsync();
    Assert.Equal(0, fixture.NextCalls);
    planning.Reply(fixture.Plan(fixture.TruckA));
    var next = await fixture.ReadNextAsync();
    fixture.AssertIdentity(next, fixture.TruckA, "");
    next.Reply("empty-ready", []);
    await selection;
    Assert.Equal(1, fixture.NextCalls);
    Assert.Single(fixture.Js.Calls, x => x.Name == "setNextLoadsBytes");
  }

  [Theory]
  [InlineData(false)]
  [InlineData(true)]
  public async Task ColdPreviewStartsNextLoadsBeforeLivePlanningOrDetailsAndReusesSavedGeometry(bool unsaved)
  {
    using var fixture = new SelectionFixture(cacheCurrentPlans: false)
      { DeferPreview = true, DeferPlanning = true, DeferDetails = true };
    var component = fixture.Render();
    component.WaitForAssertion(() => Assert.Contains(fixture.Js.Calls, x => x.Name == "setTrucks"));
    await Toggle(component, "Next loads").ChangeAsync(new ChangeEventArgs { Value = true });
    var selection = component.InvokeAsync(() => component.Instance.OnTruckSelected(fixture.TruckA.ToString()));
    var preview = await fixture.ReadPreviewAsync();
    Assert.Equal(0, fixture.PlanningCalls);
    Assert.Equal(0, fixture.NextCalls);
    var saved = fixture.Plan(fixture.TruckA);
    preview.Reply(unsaved ? saved with { State = null } : saved);
    var next = await fixture.ReadNextAsync();
    var planning = await fixture.ReadPlanningAsync();
    var details = await fixture.ReadDetailsAsync();
    fixture.AssertIdentity(next, fixture.TruckA, "");
    Assert.Equal(unsaved ? "" : $"?knownPlanId={saved.State!.Plan!.Id}&knownVersion=1", planning.Uri.Query);
    if (!unsaved)
    {
      using var current = fixture.LastCurrentPayload();
      Assert.Equal(saved.State!.Plan!.Id, current.RootElement.GetProperty("id").GetGuid());
      Assert.Equal(2, current.RootElement.GetProperty("route").GetProperty("legs")[0].GetProperty("points").GetArrayLength());
    }
    var future = fixture.FutureLoad(202);
    next.Reply("future", [future]);
    await fixture.Js.ReadNextPayloadAsync();
    fixture.AssertOnlyFuturePublished(future.Id);
    Assert.False(selection.IsCompleted);
    Assert.False(planning.Response.Task.IsCompleted);
    Assert.False(details.Response.Task.IsCompleted);
    planning.Reply(unsaved ? saved : fixture.WithoutGeometry(saved));
    details.Reply(new { id = saved.DispatchId, loadNumber = 1358 });
    await selection;
    Assert.Equal(1, fixture.PreviewCalls);
    Assert.Equal(1, fixture.PlanningCalls);
    Assert.Equal(1, fixture.NextCalls);
    Assert.Equal(2, fixture.CachedPlan(fixture.TruckA)!.State!.Plan!.Route.Legs[0].Points.Count);
  }

  [Fact]
  public async Task EmptyPreviewDoesNotInferCurrentDispatchOrLoadUnrelatedDetails()
  {
    using var fixture = new SelectionFixture(cacheCurrentPlans: false) { DeferPreview = true, DeferPlanning = true };
    var component = fixture.Render();
    component.WaitForAssertion(() => Assert.Contains(fixture.Js.Calls, x => x.Name == "setTrucks"));
    await Toggle(component, "Next loads").ChangeAsync(new ChangeEventArgs { Value = true });
    var selection = component.InvokeAsync(() => component.Instance.OnTruckSelected(fixture.TruckA.ToString()));
    var empty = new AutomaticPlanningResult(fixture.TruckA, null, null, null, null);
    (await fixture.ReadPreviewAsync()).Reply(empty);
    var planning = await fixture.ReadPlanningAsync();
    Assert.Equal(0, fixture.NextCalls);
    Assert.Equal(0, fixture.DetailsCalls);
    Assert.Equal("", planning.Uri.Query);
    planning.Reply(empty);
    await selection;
    Assert.Equal(0, fixture.NextCalls);
    Assert.Null(fixture.CachedPlan(fixture.TruckA));
    using var current = fixture.LastCurrentPayload();
    Assert.Equal(JsonValueKind.Null, current.RootElement.ValueKind);
  }

  [Fact]
  public async Task TimedOutPreviewIsCancelledAndLivePlanningContinuesWithoutWaitingForItsLateReply()
  {
    using var fixture = new SelectionFixture(cacheCurrentPlans: false) { DeferPreview = true, DeferPlanning = true };
    var component = fixture.Render();
    component.WaitForAssertion(() => Assert.Contains(fixture.Js.Calls, x => x.Name == "setTrucks"));
    await Toggle(component, "Next loads").ChangeAsync(new ChangeEventArgs { Value = true });
    var selection = component.InvokeAsync(() => component.Instance.OnTruckSelected(fixture.TruckA.ToString()));
    var preview = await fixture.ReadPreviewAsync();
    await component.InvokeAsync(() => fixture.Clock.Advance(TimeSpan.FromSeconds(2)));
    var planning = await fixture.ReadPlanningAsync();
    Assert.True(preview.Cancellation.IsCancellationRequested);
    Assert.False(preview.Response.Task.IsCompleted);
    Assert.Equal("", planning.Uri.Query);
    var latest = fixture.Plan(fixture.TruckA) with { Message = "Latest live result" };
    planning.Reply(latest);
    var next = await fixture.ReadNextAsync();
    fixture.AssertIdentity(next, fixture.TruckA, "");
    next.Reply("future", []);
    await selection;
    preview.Reply(fixture.Plan(fixture.TruckA));
    Assert.Equal("Latest live result", fixture.CachedPlan(fixture.TruckA)!.Message);
    Assert.Equal(1, fixture.PlanningCalls);
    Assert.Equal(1, fixture.NextCalls);
  }

  [Fact]
  public async Task SwitchingTruckCancelsThePendingColdPreviewAndOnlyStartsPlanningForTheNewSelection()
  {
    using var fixture = new SelectionFixture(cacheCurrentPlans: false) { DeferPreview = true };
    var component = fixture.Render();
    component.WaitForAssertion(() => Assert.Contains(fixture.Js.Calls, x => x.Name == "setTrucks"));
    await Toggle(component, "Next loads").ChangeAsync(new ChangeEventArgs { Value = true });
    var selectA = component.InvokeAsync(() => component.Instance.OnTruckSelected(fixture.TruckA.ToString()));
    var previewA = await fixture.ReadPreviewAsync();
    var selectB = component.InvokeAsync(() => component.Instance.OnTruckSelected(fixture.TruckB.ToString()));
    var previewB = await fixture.ReadPreviewAsync();
    Assert.True(previewA.Cancellation.IsCancellationRequested);
    await selectA;
    previewB.Reply(fixture.Plan(fixture.TruckB));
    var next = await fixture.ReadNextAsync();
    fixture.AssertIdentity(next, fixture.TruckB, "");
    next.Reply("b", []);
    await selectB;
    previewA.Reply(fixture.Plan(fixture.TruckA));
    Assert.Null(fixture.CachedPlan(fixture.TruckA));
    Assert.Equal(1, fixture.PlanningCalls);
    Assert.Equal(1, fixture.NextCalls);
    using var current = fixture.LastCurrentPayload();
    Assert.Equal(fixture.TruckB, current.RootElement.GetProperty("truckId").GetGuid());
  }

  [Fact]
  public async Task DisposingTheMapCancelsPendingPreviewWithoutStartingLivePlanning()
  {
    using var fixture = new SelectionFixture(cacheCurrentPlans: false) { DeferPreview = true };
    var component = fixture.Render();
    component.WaitForAssertion(() => Assert.Contains(fixture.Js.Calls, x => x.Name == "setTrucks"));
    var selection = component.InvokeAsync(() => component.Instance.OnTruckSelected(fixture.TruckA.ToString()));
    var preview = await fixture.ReadPreviewAsync();
    await component.InvokeAsync(() => component.Instance.DisposeAsync().AsTask());
    await selection;
    Assert.True(preview.Cancellation.IsCancellationRequested);
    Assert.Equal(0, fixture.PlanningCalls);
    Assert.Equal(0, fixture.NextCalls);
    preview.Reply(fixture.Plan(fixture.TruckA));
    Assert.Null(fixture.CachedPlan(fixture.TruckA));
  }

  [Theory]
  [InlineData(false)]
  [InlineData(true)]
  public async Task ReturningToTruckRestoresTheCompleteLatestSnapshotBeforeAnyNewHttpResponse(bool empty)
  {
    using var fixture = new SelectionFixture();
    var component = fixture.Render();
    component.WaitForAssertion(() => Assert.Contains(fixture.Js.Calls, x => x.Name == "setTrucks"));
    await Toggle(component, "Next loads").ChangeAsync(new ChangeEventArgs { Value = true });
    var selectA = component.InvokeAsync(() => component.Instance.OnTruckSelected(fixture.TruckA.ToString()));
    var initialA = await fixture.ReadNextAsync();
    var future = fixture.FutureLoad(202);
    initialA.Reply("geometry-a", empty ? [] : [future]);
    await selectA;

    await Toggle(component, "Next loads").ChangeAsync(new ChangeEventArgs { Value = false });
    var relabel = Toggle(component, "Next loads").ChangeAsync(new ChangeEventArgs { Value = true });
    var labels = await fixture.ReadNextAsync();
    fixture.AssertIdentity(labels, fixture.TruckA, "geometry-a");
    labels.ReplyLabels("labels-a", empty ? [] : [new(future.Id, ["Updated pickup", "Updated delivery"])]);
    await relabel;
    using (var delta = fixture.LastNextPayload())
      Assert.Equal(JsonValueKind.Null, delta.RootElement.GetProperty("routes").ValueKind);

    var selectB = component.InvokeAsync(() => component.Instance.OnTruckSelected(fixture.TruckB.ToString()));
    (await fixture.ReadNextAsync()).Reply("empty-b", []);
    await selectB;
    var published = fixture.Js.Calls.Count(x => x.Name == "setNextLoadsBytes");
    fixture.DeferPlanning = true;
    fixture.DeferDetails = true;
    var returnA = component.InvokeAsync(() => component.Instance.OnTruckSelected(fixture.TruckA.ToString()));
    var revalidate = await fixture.ReadNextAsync();
    var planning = await fixture.ReadPlanningAsync();
    var details = await fixture.ReadDetailsAsync();
    fixture.AssertIdentity(revalidate, fixture.TruckA, "labels-a");
    Assert.Equal(published + 1, fixture.Js.Calls.Count(x => x.Name == "setNextLoadsBytes"));
    using (var replay = fixture.LastNextPayload())
    {
      var routes = replay.RootElement.GetProperty("routes").EnumerateArray().ToArray();
      var names = replay.RootElement.GetProperty("labels").EnumerateArray().ToArray();
      if (empty) { Assert.Empty(routes); Assert.Empty(names); }
      else
      {
        Assert.Equal(future.Id, Assert.Single(routes).GetProperty("id").GetGuid());
        Assert.Equal(2, routes[0].GetProperty("legs")[0].GetProperty("points").GetArrayLength());
        Assert.Equal("Updated pickup", Assert.Single(names).GetProperty("names")[0].GetString());
      }
    }
    Assert.False(returnA.IsCompleted);
    Assert.False(revalidate.Response.Task.IsCompleted);
    Assert.False(planning.Response.Task.IsCompleted);
    Assert.False(details.Response.Task.IsCompleted);
    revalidate.ReplyUnchanged("labels-a");
    planning.Reply(fixture.Plan(fixture.TruckA));
    details.Reply(new { id = fixture.Plan(fixture.TruckA).DispatchId, loadNumber = 1358 });
    await returnA;
    Assert.Equal(4, fixture.NextCalls);
    Assert.Equal(published + 1, fixture.Js.Calls.Count(x => x.Name == "setNextLoadsBytes"));
  }

  private static AngleSharp.Dom.IElement Toggle(IRenderedComponent<FleetMap> component, string label) =>
    component.FindAll(".fleet-map-toggle").Single(x => x.TextContent.Trim() == label).QuerySelector("input")!;

  [Fact]
  public async Task FutureStopSelectionPinsItsDetailsAndReusesThemWithoutChangingTheCurrentRoute()
  {
    using var fixture = new SelectionFixture();
    var component = fixture.Render();
    component.WaitForAssertion(() => Assert.Contains(fixture.Js.Calls, x => x.Name == "setTrucks"));
    await component.InvokeAsync(() => component.Instance.OnTruckSelected(fixture.TruckA.ToString()));
    var toggle = Toggle(component, "Next loads").ChangeAsync(new ChangeEventArgs { Value = true });
    var future = fixture.FutureLoad(202);
    (await fixture.ReadNextAsync()).Reply("future", [future]);
    await toggle;
    var current = fixture.Plan(fixture.TruckA).DispatchId!.Value.ToString();
    var planningCalls = fixture.PlanningCalls;
    var currentHeader = "";
    await component.InvokeAsync(() => { currentHeader = component.Find("#fleet-map-details").TextContent; });
    fixture.DeferDetails = true;
    var selecting = component.InvokeAsync(() => component.Instance.OnNextLoadSelected(fixture.TruckA.ToString(), current, future.Id.ToString(), 0));
    var request = await fixture.ReadDetailsAsync();
    Assert.Equal($"/api/dispatch/{future.Id}", request.Uri.AbsolutePath);
    component.WaitForAssertion(() => Assert.Contains("202", component.Find("[aria-label='Selected next load']").TextContent));
    request.Reply(new { id = future.Id, loadNumber = 202, orderNumber = "ORDER-FUTURE", customerName = "Future customer", stops = new[]
    {
      new { sequence = 1, job = "Pick Up", name = "Future pickup", address = "10 First St", city = "Toronto", province = "ON", zipCode = "M5V 3A8", country = "Canada", scheduledDate = "2026-09-09", scheduledTime = "09:00:00", scheduledDate2 = "2026-09-09", scheduledTime2 = "14:00:00", commodity = "Machinery", notes = "Service for Load pickup" },
      new { sequence = 2, job = "Drop Off", name = "Future delivery", address = "20 Second St", city = "Chicago", province = "IL", zipCode = "60607", country = "US", scheduledDate = "2026-09-10", scheduledTime = "14:00:00", scheduledDate2 = "2026-09-10", scheduledTime2 = "16:00:00", commodity = "Machinery", notes = "Service for Load delivery" }
    } });
    await selecting;
    var details = component.Find("[aria-label='Selected next load']").TextContent;
    Assert.Contains("ORDER-FUTURE", details);
    Assert.Contains("Future customer", details);
    Assert.Contains("10 First St", details);
    Assert.Contains("Appointment", details);
    Assert.Contains("Sep 9", details);
    Assert.DoesNotContain("Machinery", details);
    Assert.DoesNotContain("Service for Load", details);
    Assert.Matches(@"ETA\s*—", details);
    await component.InvokeAsync(() =>
    {
      var card = component.Find(".fleet-map-stage [aria-label='Selected next load']");
      Assert.True(card.ClassList.Contains("fleet-map-inspector__next"));
      Assert.False(card.ClassList.Contains("fleet-map-details-card"));
      Assert.Equal("region", card.GetAttribute("role"));
      Assert.NotNull(card.Closest(".fleet-map-inspector"));
      var location = card.QuerySelector(".fleet-route-popup__location")!;
      Assert.True(location.FirstElementChild!.ClassList.Contains("fleet-map-next-load-card__header"));
      Assert.Empty(location.QuerySelectorAll("a"));
      Assert.Equal($"/dispatch/{future.Id}", card.QuerySelector(".fleet-route-popup__information > .fleet-route-popup__details-link")!.GetAttribute("href"));
      Assert.Matches(@"Appointment\s*Sep 9\s*·\s*09:00 AM\s*–\s*02:00 PM", card.QuerySelector(".fleet-route-popup__information")!.TextContent);
      Assert.DoesNotContain("Appointment", card.QuerySelector(".fleet-route-popup__location")!.TextContent);
      Assert.Equal(new[] { "10 First St", "Toronto, ON M5V 3A8, Canada" }, card.QuerySelectorAll(".fleet-route-popup__address-line").Select(x => x.TextContent));
      Assert.True(card.QuerySelector(".fleet-route-popup__information")!.TextContent.IndexOf("Appointment", StringComparison.Ordinal)
        < card.QuerySelector(".fleet-route-popup__information")!.TextContent.IndexOf("ETA", StringComparison.Ordinal));
      Assert.Single(System.Text.RegularExpressions.Regex.Matches(card.TextContent, "Appointment"));
      Assert.Empty(component.FindAll("#fleet-map-details [aria-label='Selected next load']"));
      Assert.Single(component.FindAll("#fleet-map-details [aria-label='Current dispatch route']"));
      Assert.Equal(currentHeader, component.Find("#fleet-map-details").TextContent);
      Assert.True(component.Find("#fleet-map-details").HasAttribute("hidden"));
      Assert.Contains("Next load stop", component.Find(".fleet-map-inspector__header").TextContent);
      Assert.Empty(component.FindAll(".fleet-map-details-card"));
      Assert.Contains(component.FindAll("#fleet-map-details a"), link => link.GetAttribute("href") == $"/dispatch/{current}");
      Assert.Contains(card.QuerySelectorAll("a"), link => link.GetAttribute("href") == $"/dispatch/{future.Id}");
    });
    using (var route = fixture.LastCurrentPayload())
      Assert.Equal(fixture.Plan(fixture.TruckA).State!.Plan!.Id, route.RootElement.GetProperty("id").GetGuid());
    var detailCalls = fixture.DetailsCalls;
    await component.InvokeAsync(() => component.Instance.OnNextLoadSelected(fixture.TruckA.ToString(), current, future.Id.ToString(), 1));
    Assert.Contains("Future delivery", component.Find("[aria-label='Selected next load']").TextContent);
    Assert.Contains("Sep 10", component.Find("[aria-label='Selected next load']").TextContent);
    Assert.DoesNotContain("Machinery", component.Find("[aria-label='Selected next load']").TextContent);
    Assert.DoesNotContain("Service for Load", component.Find("[aria-label='Selected next load']").TextContent);
    Assert.Equal(detailCalls, fixture.DetailsCalls);
    await component.InvokeAsync(() => component.Instance.OnNextLoadSelected(fixture.TruckA.ToString(), current, null, 0));
    Assert.Single(component.FindAll("[aria-label='Current dispatch route']"));
    await component.InvokeAsync(() => component.Instance.OnNextLoadSelected(fixture.TruckA.ToString(), current, future.Id.ToString(), 0));
    Assert.Equal(detailCalls, fixture.DetailsCalls);
    await component.InvokeAsync(() => component.Instance.OnTruckSelected(fixture.TruckA.ToString()));
    Assert.Empty(component.FindAll("[aria-label='Selected next load']"));
    Assert.Single(component.FindAll("[aria-label='Current dispatch route']"));
    Assert.Equal(planningCalls, fixture.PlanningCalls);
  }

  [Theory]
  [InlineData(false)]
  [InlineData(true)]
  public async Task ClosingFutureDetailsCancelsPendingReplyWithoutChangingTheCurrentSelection(bool escape)
  {
    using var fixture = new SelectionFixture();
    var component = fixture.Render();
    component.WaitForAssertion(() => Assert.Contains(fixture.Js.Calls, x => x.Name == "setTrucks"));
    await component.InvokeAsync(() => component.Instance.OnTruckSelected(fixture.TruckA.ToString()));
    var toggle = component.InvokeAsync(() => Toggle(component, "Next loads").ChangeAsync(new ChangeEventArgs { Value = true }));
    var future = fixture.FutureLoad(202);
    (await fixture.ReadNextAsync()).Reply("future", [future]);
    await toggle;
    var current = fixture.Plan(fixture.TruckA).DispatchId!.Value.ToString();
    var currentHeader = "";
    await component.InvokeAsync(() => { currentHeader = component.Find("#fleet-map-details").TextContent; });
    fixture.DeferDetails = true;
    var selecting = component.InvokeAsync(() => component.Instance.OnNextLoadSelected(fixture.TruckA.ToString(), current, future.Id.ToString(), 0));
    var pending = await fixture.ReadDetailsAsync();
    var httpCalls = fixture.HttpCalls;
    var mapCalls = fixture.Js.Calls.Count;

    await component.InvokeAsync(async () =>
    {
      var card = component.Find(".fleet-map-stage [aria-label='Selected next load']");
      Assert.Contains("Loading load details", card.TextContent);
      if (escape) await component.Find(".fleet-map-inspector").KeyDownAsync(new KeyboardEventArgs { Key = "Escape" });
      else await component.Find(".fleet-map-inspector__back").ClickAsync(new MouseEventArgs());
      Assert.Empty(component.FindAll("[aria-label='Selected next load']"));
      Assert.Equal(currentHeader, component.Find("#fleet-map-details").TextContent);
      Assert.True(Toggle(component, "Next loads").HasAttribute("checked"));
      Assert.Equal("false", component.Find(".fleet-map-mobile-summary__toggle").GetAttribute("aria-expanded"));
    });
    Assert.True(pending.Cancellation.IsCancellationRequested);
    Assert.Equal(httpCalls, fixture.HttpCalls);
    var transition = Assert.Single(fixture.Js.Calls.Skip(mapCalls));
    Assert.Equal("setInspectorMode", transition.Name);
    Assert.Equal("truck", transition.Args![0]);

    pending.Reply(new { id = future.Id, loadNumber = 202, customerName = "Obsolete closed details" });
    await selecting;
    await component.InvokeAsync(() =>
    {
      Assert.Empty(component.FindAll("[aria-label='Selected next load']"));
      Assert.DoesNotContain("Obsolete closed details", component.Markup);
      Assert.Equal(currentHeader, component.Find("#fleet-map-details").TextContent);
    });
    Assert.Equal(httpCalls, fixture.HttpCalls);
    using var route = fixture.LastCurrentPayload();
    Assert.Equal(fixture.Plan(fixture.TruckA).State!.Plan!.Id, route.RootElement.GetProperty("id").GetGuid());
  }

  [Fact]
  public async Task LateFutureDetailsCannotReplaceThePanelAfterChangingTrucks()
  {
    using var fixture = new SelectionFixture();
    var component = fixture.Render();
    component.WaitForAssertion(() => Assert.Contains(fixture.Js.Calls, x => x.Name == "setTrucks"));
    await component.InvokeAsync(() => component.Instance.OnTruckSelected(fixture.TruckA.ToString()));
    var toggle = Toggle(component, "Next loads").ChangeAsync(new ChangeEventArgs { Value = true });
    var future = fixture.FutureLoad(202);
    (await fixture.ReadNextAsync()).Reply("future", [future]);
    await toggle;
    fixture.DeferDetails = true;
    var selecting = component.InvokeAsync(() => component.Instance.OnNextLoadSelected(fixture.TruckA.ToString(),
      fixture.Plan(fixture.TruckA).DispatchId!.Value.ToString(), future.Id.ToString(), 0));
    var details = await fixture.ReadDetailsAsync();
    fixture.DeferDetails = false;
    var otherTruck = component.InvokeAsync(() => component.Instance.OnTruckSelected(fixture.TruckB.ToString()));
    (await fixture.ReadNextAsync()).Reply("empty-b", []);
    await otherTruck;
    Assert.True(details.Cancellation.IsCancellationRequested);
    details.Reply(new { id = future.Id, loadNumber = 202, customerName = "Obsolete future details" });
    await selecting;
    Assert.DoesNotContain("Obsolete future details", component.Markup);
    Assert.Empty(component.FindAll("[aria-label='Selected next load']"));
    Assert.Contains("64888", component.Find(".fleet-map-truck-info").TextContent);
    using var route = fixture.LastCurrentPayload();
    Assert.Equal(fixture.TruckB, route.RootElement.GetProperty("truckId").GetGuid());
  }

  [Theory]
  [InlineData(false)]
  [InlineData(true)]
  public async Task FuturePanelUsesOnlyTheSelectedStopFreshEtaFromSavedDetailsOrTheCurrentChain(bool fromChain)
  {
    using var fixture = new SelectionFixture();
    var future = fixture.FutureLoad(202);
    var pickupId = future.Stops[0].Id;
    var deliveryId = future.Stops[1].Id;
    var now = fixture.Clock.GetUtcNow().UtcDateTime;
    var arrival = new DateTimeOffset(2026, 9, 10, 14, 0, 0, TimeSpan.FromHours(-4));
    var eta = new DispatchEta(now, now.AddMinutes(2),
      [new(deliveryId, arrival.AddHours(-5), "America/Toronto", null, null, 20, 0) { DispatchId = Guid.NewGuid() },
        new(pickupId, arrival.AddHours(-3), "America/Toronto", null, null, 20, 0) { DispatchId = future.Id },
        new(deliveryId, arrival, "America/Toronto", null, null, 60, 0) { DispatchId = future.Id }], null, []);
    if (fromChain) fixture.SetEta(fixture.TruckA, eta);
    var component = fixture.Render();
    component.WaitForAssertion(() => Assert.Contains(fixture.Js.Calls, x => x.Name == "setTrucks"));
    await component.InvokeAsync(() => component.Instance.OnTruckSelected(fixture.TruckA.ToString()));
    var toggle = Toggle(component, "Next loads").ChangeAsync(new ChangeEventArgs { Value = true });
    (await fixture.ReadNextAsync()).Reply("future", [future]);
    await toggle;
    var current = fixture.Plan(fixture.TruckA).DispatchId!.Value.ToString();
    fixture.DeferDetails = true;
    var selecting = component.InvokeAsync(() => component.Instance.OnNextLoadSelected(fixture.TruckA.ToString(), current, future.Id.ToString(), 1));
    var request = await fixture.ReadDetailsAsync();
    request.Reply(new Client.Models.DTO.Dispatch.DispatchResponse
    {
      Id = future.Id, LoadNumber = future.LoadNumber, Eta = fromChain ? null : eta,
      Stops = [new() { Id = pickupId, Sequence = 1, Job = "Pick Up", Name = "Pickup" },
        new() { Id = deliveryId, Sequence = 2, Job = "Drop Off", Name = "Delivery" }]
    });
    await selecting;
    var details = component.Find("[aria-label='Selected next load']").TextContent;
    Assert.Contains("ETA", details);
    Assert.Contains("02:00 PM", details);
    Assert.DoesNotContain("11:00 AM", details);
    Assert.DoesNotContain("09:00 AM", details);
    var inspected = component.Find("[aria-label='Selected next load']");
    Assert.Single(System.Text.RegularExpressions.Regex.Matches(inspected.TextContent, "Appointment"));
    Assert.DoesNotContain("Appointment", inspected.QuerySelector(".fleet-route-popup__location")!.TextContent);
    Assert.Contains("Appointment", inspected.QuerySelector(".fleet-route-popup__information")!.TextContent);
    Assert.Contains("02:00 PM", inspected.QuerySelector(".fleet-route-popup__information")!.TextContent);
    var calls = fixture.DetailsCalls;
    await component.InvokeAsync(() => fixture.Clock.Advance(TimeSpan.FromMinutes(3)));
    (await fixture.ReadNextAsync()).ReplyUnchanged("future");
    component.WaitForAssertion(() =>
    {
      using var payload = JsonSerializer.SerializeToDocument(fixture.Js.Calls.Last(x => x.Name == "setStopEtas").Args![0]);
      Assert.False(payload.RootElement.GetProperty("Refreshing").GetBoolean());
    });
    await component.InvokeAsync(() => component.Instance.OnNextLoadSelected(fixture.TruckA.ToString(), current, future.Id.ToString(), 0));
    var pickupDetails = component.Find("[aria-label='Selected next load']").TextContent;
    if (fromChain)
    {
      Assert.Contains("11:00 AM", pickupDetails);
      Assert.DoesNotContain("02:00 PM", pickupDetails);
      Assert.DoesNotContain("09:00 AM", pickupDetails);
    }
    else Assert.Matches(@"ETA\s*—", pickupDetails);
    Assert.Equal(calls, fixture.DetailsCalls);
    if (fromChain)
    {
      await component.InvokeAsync(() => fixture.Clock.Advance(TimeSpan.FromMinutes(14)));
      (await fixture.ReadNextAsync()).ReplyUnchanged("future");
      component.Render();
      component.WaitForAssertion(() => Assert.Matches(@"ETA\s*—", component.Find("[aria-label='Selected next load']").TextContent));
      Assert.Equal(calls, fixture.DetailsCalls);
    }
  }

  [Fact]
  public async Task FutureStopDistanceIncludesCurrentRemainingAndEveryPrecedingLoad()
  {
    using var fixture = new SelectionFixture();
    var plan = fixture.Plan(fixture.TruckA).State!.Plan!;
    plan.OriginalPlannedMiles = 100;
    plan.Route.Legs = [new(100, 6000, [new(40, -80), new(41, -79)])];
    var first = fixture.FutureLoad(202) with
    {
      Deadhead = new(15, []),
      Legs = [new(30, 1800, [new(40, -80), new(41, -79)])]
    };
    var second = fixture.FutureLoad(303) with
    {
      Deadhead = new(20, []),
      Legs = [new(40, 2400, [new(41, -79), new(42, -78)])]
    };
    var component = fixture.Render();
    component.WaitForAssertion(() => Assert.Contains(fixture.Js.Calls, x => x.Name == "setTrucks"));
    await component.InvokeAsync(() => component.Instance.OnTruckSelected(fixture.TruckA.ToString()));
    await component.InvokeAsync(() => component.Instance.OnRouteProgress(fixture.TruckA.ToString(), 90, 10));
    var toggle = component.InvokeAsync(() => Toggle(component, "Next loads").ChangeAsync(new ChangeEventArgs { Value = true }));
    (await fixture.ReadNextAsync()).Reply("future", [first, second]);
    await toggle;
    var current = plan.DispatchId.ToString();

    foreach (var (load, stop, expected, leg) in new[]
    {
      (first, 0, 105d, "15 mi · 24 km"), (first, 1, 135d, "30 mi · 48 km"),
      (second, 0, 155d, "20 mi · 32 km"), (second, 1, 195d, "40 mi · 64 km")
    })
    {
      await component.InvokeAsync(() => component.Instance.OnNextLoadSelected(fixture.TruckA.ToString(), current, load.Id.ToString(), stop));
      await component.InvokeAsync(() =>
      {
        Assert.Equal((double?)expected, component.FindComponent<NextLoadDetailsCard>().Instance.DistanceMiles);
        var metrics = component.Find("[aria-label='Selected next load'] .fleet-route-popup__information .fleet-map-next-load-card__metrics");
        Assert.Equal(new[] { stop == 0 ? "Empty" : "Leg", "Total" }, metrics.QuerySelectorAll("dt").Select(x => x.TextContent));
        Assert.Equal(leg, metrics.QuerySelector("dd")!.TextContent);
        Assert.DoesNotContain("Load Distance", metrics.TextContent);
      });
    }
    await component.InvokeAsync(() =>
    {
      var card = component.Find("[aria-label='Selected next load']").TextContent;
      Assert.Contains("195 mi", card);
      Assert.Contains("314 km", card);
      Assert.Contains("Appointment", card);
      Assert.Matches(@"ETA\s*—", card);
    });
    var calls = fixture.HttpCalls;
    await component.InvokeAsync(() => component.Instance.OnRouteProgress(fixture.TruckA.ToString(), 80, 20));
    await component.InvokeAsync(() => Assert.Equal((double?)185d, component.FindComponent<NextLoadDetailsCard>().Instance.DistanceMiles));
    Assert.Equal(calls, fixture.HttpCalls);
  }

  [Theory]
  [InlineData("missing-eta")]
  [InlineData("empty-eta")]
  [InlineData("missing-plan")]
  public async Task RecalculationRetainsTheSameEtaAndDisplayedDistanceInHeaderFutureCardAndMap(string missing)
  {
    using var fixture = new SelectionFixture();
    var now = fixture.Clock.GetUtcNow().UtcDateTime;
    var plan = fixture.Plan(fixture.TruckA).State!.Plan!;
    var currentStop = new PlanStop(Guid.NewGuid(), "Current delivery", "123 Main Street", 1, new(40, -80));
    plan.Stops = [currentStop];
    plan.Tracking.NextStopId = currentStop.Id;
    var future = fixture.FutureLoad(202) with { Deadhead = new(15, []) };
    var forecast = new DispatchEta(now, now.AddMinutes(2),
      [new(currentStop.Id, now.AddHours(1), "UTC", null, 0, 60, 0) { DispatchId = plan.DispatchId },
        new(future.Stops[1].Id, now.AddHours(2), "UTC", null, 0, 120, 0) { DispatchId = future.Id }], null, []);
    fixture.SetEta(fixture.TruckA, forecast);
    var saved = fixture.Plan(fixture.TruckA);
    var component = fixture.Render();
    component.WaitForAssertion(() => Assert.Contains(fixture.Js.Calls, x => x.Name == "setTrucks"));
    await component.InvokeAsync(() => component.Instance.OnTruckSelected(fixture.TruckA.ToString()));
    await component.InvokeAsync(() => component.Instance.OnRouteProgress(fixture.TruckA.ToString(), 90, 10));
    var toggle = component.InvokeAsync(() => Toggle(component, "Next loads").ChangeAsync(new ChangeEventArgs { Value = true }));
    (await fixture.ReadNextAsync()).Reply("future", [future]);
    await toggle;
    await component.InvokeAsync(() => component.Instance.OnNextLoadSelected(fixture.TruckA.ToString(),
      plan.DispatchId.ToString(), future.Id.ToString(), 1));
    Assert.Equal(115, component.FindComponent<NextLoadDetailsCard>().Instance.DistanceMiles);
    var previousCurrentEta = component.Find("[aria-label='Current dispatch route'] .arrival-estimate").OuterHtml;
    var previousFutureEta = component.Find("[aria-label='Selected next load'] .arrival-estimate").OuterHtml;

    var pending = saved with { State = saved.State! with
    {
      Plan = missing == "missing-plan" ? null : saved.State!.Plan,
      Eta = missing == "missing-eta" ? null : forecast with { Stops = [], RouteUpdatePending = true }
    } };
    fixture.SetPlanningResult(fixture.TruckA, pending);
    await component.InvokeAsync(() => fixture.Clock.Advance(TimeSpan.FromSeconds(10)));
    (await fixture.ReadNextAsync()).ReplyUnchanged("future");
    component.WaitForAssertion(() =>
    {
      Assert.True(component.FindComponent<NextLoadDetailsCard>().Instance.Eta?.RouteUpdatePending);
      using var payload = JsonSerializer.SerializeToDocument(fixture.Js.Calls.Last(x => x.Name == "setStopEtas").Args![0]);
      Assert.False(payload.RootElement.GetProperty("Refreshing").GetBoolean());
      Assert.IsType<RouteProgress>(fixture.Js.Calls.Last(x => x.Name == "setRouteBytes").Args![1]);
    });
    await component.InvokeAsync(() => component.Instance.OnRouteProgress(fixture.TruckA.ToString(), 80, 20));
    Assert.Equal(115, component.FindComponent<NextLoadDetailsCard>().Instance.DistanceMiles);
    Assert.Equal(previousCurrentEta, component.Find("[aria-label='Current dispatch route'] .arrival-estimate").OuterHtml);
    Assert.Equal(previousFutureEta, component.Find("[aria-label='Selected next load'] .arrival-estimate").OuterHtml);
    Assert.Equal(2, component.FindAll(".arrival-estimate__ontime").Count);
    Assert.DoesNotContain("Updating", component.Markup);
    var progress = Assert.IsType<RouteProgress>(fixture.Js.Calls.Last(x => x.Name == "setRouteBytes").Args![1]);
    Assert.Equal(90, progress.RemainingMiles);
    using var published = JsonSerializer.SerializeToDocument(fixture.Js.Calls.Last(x => x.Name == "setStopEtas").Args![0]);
    var publishedEta = published.RootElement.GetProperty("Eta").Deserialize<DispatchEta>()!;
    Assert.Equal(forecast.Stops, publishedEta.Stops);
    Assert.Equal(forecast.ValidUntil, publishedEta.ValidUntil);
    Assert.True(publishedEta.RouteUpdatePending);

    await component.InvokeAsync(() => fixture.Clock.Advance(TimeSpan.FromMinutes(17)));
    (await fixture.ReadNextAsync()).ReplyUnchanged("future");
    component.WaitForAssertion(() => Assert.Null(component.FindComponent<NextLoadDetailsCard>().Instance.Eta));
    Assert.Null(component.FindComponent<NextLoadDetailsCard>().Instance.DistanceMiles);
    var refreshedAt = fixture.Clock.GetUtcNow().UtcDateTime;
    var fresh = forecast with { CalculatedAt = refreshedAt, ValidUntil = refreshedAt.AddMinutes(2),
      Stops = forecast.Stops.Select(stop => stop with { Arrival = stop.Arrival.AddMinutes(5) }).ToArray() };
    fixture.SetPlanningResult(fixture.TruckA, saved with { State = saved.State! with { Eta = fresh } });
    await component.InvokeAsync(() => fixture.Clock.Advance(TimeSpan.FromSeconds(10)));
    (await fixture.ReadNextAsync()).ReplyUnchanged("future");
    component.WaitForAssertion(() =>
    {
      Assert.Equal(fresh.Stops[1].Arrival, component.FindComponent<NextLoadDetailsCard>().Instance.Eta?.Stops[0].Arrival);
      Assert.DoesNotContain("Updating", component.Find("[aria-label='Selected next load']").TextContent);
    });
    await component.InvokeAsync(() => component.Instance.OnRouteProgress(fixture.TruckA.ToString(), 80, 20));
    Assert.Equal(105, component.FindComponent<NextLoadDetailsCard>().Instance.DistanceMiles);
    Assert.Equal(fresh.Stops[1].Arrival, component.FindComponent<NextLoadDetailsCard>().Instance.Eta!.Stops[0].Arrival);
  }

  [Theory]
  [InlineData("truck")]
  [InlineData("dispatch")]
  [InlineData("next-stop")]
  [InlineData("not-pending")]
  public async Task MissingPlanCannotReuseAPreviousRouteForDifferentOrUnavailableOwnership(string changed)
  {
    using var fixture = new SelectionFixture();
    var now = fixture.Clock.GetUtcNow().UtcDateTime;
    var saved = fixture.Plan(fixture.TruckA);
    var stop = new PlanStop(Guid.NewGuid(), "Delivery", "Warehouse", 1, new(40, -80));
    saved.State!.Plan!.Stops = [stop];
    saved.State.Plan.Tracking.NextStopId = stop.Id;
    var eta = new DispatchEta(now, now.AddMinutes(2),
      [new(stop.Id, now.AddHours(1), "UTC", null, 0, 60, 0) { DispatchId = saved.DispatchId!.Value }], null, []);
    fixture.SetEta(fixture.TruckA, eta);
    var component = fixture.Render();
    component.WaitForAssertion(() => Assert.Contains(fixture.Js.Calls, x => x.Name == "setTrucks"));
    await component.InvokeAsync(() => component.Instance.OnTruckSelected(fixture.TruckA.ToString()));
    Assert.Single(component.FindAll("[aria-label='Current dispatch route']"));
    var pending = saved with
    {
      TruckId = changed == "truck" ? fixture.TruckB : saved.TruckId,
      DispatchId = changed == "dispatch" ? Guid.NewGuid() : saved.DispatchId,
      State = saved.State with { Plan = null, Eta = eta with
      {
        RouteUpdatePending = changed != "not-pending",
        Stops = changed == "next-stop" ? [eta.Stops[0] with { StopId = Guid.NewGuid() }] : []
      } }
    };
    fixture.SetPlanningResult(fixture.TruckA, pending);
    await component.InvokeAsync(() => fixture.Clock.Advance(TimeSpan.FromSeconds(10)));
    component.WaitForAssertion(() =>
    {
      var panel = component.Find("[aria-label='Current dispatch route']");
      Assert.Equal(2, panel.QuerySelectorAll(".fleet-map-route-info__metric").Length);
      Assert.All(panel.QuerySelectorAll(".fleet-map-route-info__metric strong"),
        value => Assert.Equal("— mi", value.TextContent));
      Assert.Empty(panel.QuerySelectorAll(".arrival-estimate__ontime"));
      Assert.DoesNotContain("Warehouse", panel.TextContent);
      using var payload = fixture.LastCurrentPayload();
      Assert.Equal(JsonValueKind.Null, payload.RootElement.ValueKind);
    });
    using var published = fixture.LastCurrentPayload();
    Assert.Equal(JsonValueKind.Null, published.RootElement.ValueKind);
  }

  [Theory]
  [InlineData("remaining")]
  [InlineData("deadhead")]
  [InlineData("leg")]
  public async Task UnknownFutureDistanceDoesNotShowPartialTotals(string unknown)
  {
    using var fixture = new SelectionFixture();
    var first = fixture.FutureLoad(202) with
    {
      Deadhead = unknown == "deadhead" ? null : new(15, []),
      Legs = unknown == "leg" ? [] : [new(30, 1800, [new(40, -80), new(41, -79)])]
    };
    var second = fixture.FutureLoad(303) with { Deadhead = new(20, []) };
    var component = fixture.Render();
    component.WaitForAssertion(() => Assert.Contains(fixture.Js.Calls, x => x.Name == "setTrucks"));
    await component.InvokeAsync(() => component.Instance.OnTruckSelected(fixture.TruckA.ToString()));
    if (unknown != "remaining")
      await component.InvokeAsync(() => component.Instance.OnRouteProgress(fixture.TruckA.ToString(), 90, 10));
    var toggle = component.InvokeAsync(() => Toggle(component, "Next loads").ChangeAsync(new ChangeEventArgs { Value = true }));
    (await fixture.ReadNextAsync()).Reply("future", [first, second]);
    await toggle;
    await component.InvokeAsync(() => component.Instance.OnNextLoadSelected(fixture.TruckA.ToString(),
      fixture.Plan(fixture.TruckA).DispatchId!.Value.ToString(), second.Id.ToString(), 1));
    await component.InvokeAsync(() =>
    {
      Assert.Null(component.FindComponent<NextLoadDetailsCard>().Instance.DistanceMiles);
      var information = component.Find("[aria-label='Selected next load'] .fleet-route-popup__information").TextContent;
      Assert.Matches(@"Total\s*—", information);
      Assert.Matches(@"Leg\s*10 mi\s*·\s*16 km", information);
    });
  }

  [Fact]
  public void FutureDetailsShowTheSelectedStopsPrecedingLegInsteadOfAllLoadedMiles()
  {
    using var context = new BunitContext();
    context.Services.AddSingleton<TimeProvider>(new FakeTimeProvider());
    var route = new NextLoadRoute(Guid.NewGuid(), 202, "ready",
      [new(10, 600, []), new(20, 1200, []), new(30, 1800, [])],
      [new(40, -80, "Pickup"), new(41, -79, "Delivery"), new(42, -78, "Delivery"), new(43, -77, "Delivery")], new(15, []));
    var component = context.Render<NextLoadDetailsCard>(p => p.Add(x => x.Route, route)
      .Add(x => x.StopIndex, 2).Add(x => x.DistanceMiles, 105d));
    var metrics = component.Find(".fleet-map-next-load-card__metrics");
    Assert.Equal(new[] { "Leg", "Total" }, metrics.QuerySelectorAll("dt").Select(x => x.TextContent));
    Assert.Equal(new[] { "20 mi · 32 km", "105 mi · 169 km" }, metrics.QuerySelectorAll("dd").Select(x => x.TextContent));
    component.Render(p => p.Add(x => x.StopIndex, 4));
    Assert.Equal("—", component.Find(".fleet-map-next-load-card__metrics dd").TextContent);
  }

  [Fact]
  public async Task FutureDetailsSplitAddressForDisplayButCopyTheOriginalWholeValue()
  {
    using var context = new BunitContext();
    context.Services.AddSingleton<TimeProvider>(new FakeTimeProvider());
    var route = new NextLoadRoute(Guid.NewGuid(), 1373, "ready", [], []);
    var stop = new PlanStop(Guid.NewGuid(), "Warehouse", " 308 Springhill Farm Rd, building 3, Fort Mill, SC 29715, US ", 1, new(40, -80));
    string? copied = null;
    var component = context.Render<NextLoadDetailsCard>(p => p.Add(x => x.Route, route).Add(x => x.Stop, stop)
      .Add(x => x.OnCopy, value => copied = value));
    Assert.Equal(new[] { "308 Springhill Farm Rd, building 3", "Fort Mill, SC 29715, US" },
      component.FindAll(".fleet-route-popup__address-line").Select(x => x.TextContent));
    await component.Find("[title='Copy full address']").ClickAsync(new MouseEventArgs());
    Assert.Equal(stop.Address, copied);

    var changed = stop with { Address = "530 Henry St, Rome, NY, 13440, US" };
    component.Render(p => p.Add(x => x.Stop, changed));
    Assert.Equal(new[] { "530 Henry St", "Rome, NY 13440, US" },
      component.FindAll(".fleet-route-popup__address-line").Select(x => x.TextContent));
    await component.Find("[title='Copy full address']").ClickAsync(new MouseEventArgs());
    Assert.Equal(changed.Address, copied);
    component.Render(p => p.Add(x => x.Stop, changed with { Address = "" }));
    Assert.Empty(component.FindAll("[title='Copy full address']"));
  }

  [Fact]
  public void FutureDetailsShowOnlyTheSelectedJobsAppointmentReferenceWithoutFullNotes()
  {
    using var context = new BunitContext();
    context.Services.AddSingleton<TimeProvider>(new FakeTimeProvider());
    var route = new NextLoadRoute(Guid.NewGuid(), 1373, "ready", [], []);
    var stop = new PlanStop(Guid.NewGuid(), "Warehouse", "530 Henry St, Rome, NY 13440, US", 1, new(40, -80))
    {
      Job = "Pick Up", Commodity = "STEELCOILS",
      Notes = "Shipper appointment confirmation number: PU123456. Receiver appointment confirmation number: DL654321. Shipper BOL: 42845601. Service for Load sentinel."
    };
    var component = context.Render<NextLoadDetailsCard>(p => p.Add(x => x.Route, route).Add(x => x.Stop, stop));
    var reference = component.Find(".fleet-route-popup__location .fleet-route-popup__reference");
    Assert.Equal(new[] { "Appt #", "PU123456" }, reference.Children.Select(x => x.TextContent));
    Assert.True(reference.ClassList.Contains("fleet-route-popup__section-start"));
    Assert.StartsWith("Pick Up · Stop 1 of", component.Find(".fleet-route-popup__kind").TextContent);
    Assert.True(component.Find(".fleet-map-next-load-card__metrics").ClassList.Contains("fleet-route-popup__section-start"));
    Assert.DoesNotContain("DL654321", component.Markup);
    Assert.DoesNotContain("42845601", component.Markup);
    Assert.DoesNotContain("STEELCOILS", component.Markup);
    Assert.DoesNotContain("Service for Load", component.Markup);

    component.Render(p => p.Add(x => x.Stop, stop with { Job = "Drop Off" }));
    Assert.Equal(new[] { "Appt #", "DL654321" }, component.Find(".fleet-route-popup__reference").Children.Select(x => x.TextContent));
    Assert.DoesNotContain("PU123456", component.Markup);
    component.Render(p => p.Add(x => x.Stop, stop with { Job = "Drop Off", Notes = "Shipper BOL: 42845601. Service for Load sentinel." }));
    Assert.Empty(component.FindAll(".fleet-route-popup__reference"));
    Assert.DoesNotContain("42845601", component.Markup);
    Assert.DoesNotContain("Service for Load", component.Markup);
  }

  [Fact]
  public async Task FutureSelectionSurvivesAnUnchangedPollButClearsWhenTheLoadDisappearsOrNextLoadsAreHidden()
  {
    using var fixture = new SelectionFixture();
    var component = fixture.Render();
    component.WaitForAssertion(() => Assert.Contains(fixture.Js.Calls, x => x.Name == "setTrucks"));
    await component.InvokeAsync(() => component.Instance.OnTruckSelected(fixture.TruckA.ToString()));
    var toggle = component.InvokeAsync(() => Toggle(component, "Next loads").ChangeAsync(new ChangeEventArgs { Value = true }));
    var future = fixture.FutureLoad(202);
    (await fixture.ReadNextAsync()).Reply("future", [future]);
    await toggle;
    var current = fixture.Plan(fixture.TruckA).DispatchId!.Value.ToString();
    await component.InvokeAsync(() => component.Instance.OnNextLoadSelected(fixture.TruckA.ToString(), current, future.Id.ToString(), 0));
    await component.InvokeAsync(() => fixture.Clock.Advance(TimeSpan.FromSeconds(10)));
    (await fixture.ReadNextAsync()).ReplyUnchanged("future");
    component.WaitForAssertion(() =>
    {
      Assert.Single(component.FindAll("[aria-label='Selected next load']"));
      using var payload = JsonSerializer.SerializeToDocument(fixture.Js.Calls.Last(x => x.Name == "setStopEtas").Args![0]);
      Assert.False(payload.RootElement.GetProperty("Refreshing").GetBoolean());
    });
    await component.InvokeAsync(() => Toggle(component, "Next loads").ChangeAsync(new ChangeEventArgs { Value = false }));
    Assert.Empty(component.FindAll("[aria-label='Selected next load']"));
    var reopen = component.InvokeAsync(() => Toggle(component, "Next loads").ChangeAsync(new ChangeEventArgs { Value = true }));
    (await fixture.ReadNextAsync()).ReplyUnchanged("future");
    await reopen;
    await component.InvokeAsync(() => component.Instance.OnNextLoadSelected(fixture.TruckA.ToString(), current, future.Id.ToString(), 0));
    await component.InvokeAsync(() => fixture.Clock.Advance(TimeSpan.FromSeconds(10)));
    (await fixture.ReadNextAsync()).Reply("empty", []);
    component.WaitForAssertion(() => Assert.Empty(component.FindAll("[aria-label='Selected next load']")));
    Assert.Single(component.FindAll("[aria-label='Current dispatch route']"));
  }

  private sealed class SelectionFixture : IDisposable
  {
    public Guid TruckA { get; } = Guid.NewGuid();
    public Guid TruckB { get; } = Guid.NewGuid();
    public FakeTimeProvider Clock { get; } = new();
    public MapJs Js { get; } = new();
    public bool DeferPlanning { get; set; }
    public bool DeferDetails { get; set; }
    public bool DeferPreview { get; set; }
    public bool PreviewUnavailable { get; set; }
    public int HttpCalls;
    public int NextCalls;
    public int PlanningCalls;
    public int PreviewCalls;
    public int DetailsCalls;
    private readonly ClientComponentContext _context;
    private readonly CancellationTokenSource _shutdown = new();
    private readonly Dictionary<Guid, AutomaticPlanningResult> _plans = [];
    private readonly Dictionary<Guid, NextLoadRoute> _futureLoads = [];
    private readonly Channel<PendingNextRequest> _next = Channel.CreateUnbounded<PendingNextRequest>();
    private readonly Channel<PendingHttpRequest> _planning = Channel.CreateUnbounded<PendingHttpRequest>();
    private readonly Channel<PendingHttpRequest> _details = Channel.CreateUnbounded<PendingHttpRequest>();
    private readonly Channel<PendingHttpRequest> _preview = Channel.CreateUnbounded<PendingHttpRequest>();
    public SelectionFixture(bool cacheCurrentPlans = true)
    {
      foreach (var truck in new[] { TruckA, TruckB })
      {
        var dispatch = Guid.NewGuid();
        _plans[truck] = new(truck, dispatch, 1358,
          new(new(), new() { Id = Guid.NewGuid(), DispatchId = dispatch, TruckId = truck, Version = 1,
            Route = new() { Legs = [new(10, 600, [new(40, -80), new(41, -79)])] } }, null, null, null, true), null);
      }
      _context = new ClientComponentContext(RespondAsync);
      _context.Services.AddSingleton<TimeProvider>(Clock);
      _context.Services.AddSingleton<IJSRuntime>(Js);
      _context.Services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());
      var cache = _context.Services.GetRequiredService<PlanningDisplayCache>();
      if (cacheCurrentPlans)
        foreach (var (truck, plan) in _plans) cache.Store($"api/fleet/trucks/{truck}/planning", plan);
    }
    public IRenderedComponent<FleetMap> Render(Guid? dispatchId = null)
    {
      if (dispatchId is { } id)
        _context.Services.GetRequiredService<NavigationManager>().NavigateTo($"/fleet/map?dispatchId={id}");
      return _context.Render<FleetMap>();
    }
    public Task<PendingNextRequest> ReadNextAsync() => _next.Reader.ReadAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));
    public Task<PendingHttpRequest> ReadPlanningAsync() => _planning.Reader.ReadAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));
    public Task<PendingHttpRequest> ReadDetailsAsync() => _details.Reader.ReadAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));
    public Task<PendingHttpRequest> ReadPreviewAsync() => _preview.Reader.ReadAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));
    public AutomaticPlanningResult Plan(Guid truck) => _plans[truck];
    public void SetEta(Guid truck, DispatchEta? eta)
    {
      SetPlanningResult(truck, _plans[truck] with { State = _plans[truck].State! with { Eta = eta } });
    }
    public void SetPlanningResult(Guid truck, AutomaticPlanningResult result)
    {
      _plans[truck] = result;
      _context.Services.GetRequiredService<PlanningDisplayCache>().Store($"api/fleet/trucks/{truck}/planning", _plans[truck]);
    }
    public AutomaticPlanningResult? CachedPlan(Guid truck) => _context.Services.GetRequiredService<PlanningDisplayCache>()
      .Get($"api/fleet/trucks/{truck}/planning");
    public AutomaticPlanningResult? CachedDispatchPlan(Guid dispatch) => _context.Services.GetRequiredService<PlanningDisplayCache>()
      .Get($"api/dispatch/{dispatch}/planning/automatic");
    public void StoreDispatchAlias(AutomaticPlanningResult result) => _context.Services.GetRequiredService<PlanningDisplayCache>()
      .Store($"api/dispatch/{result.DispatchId}/planning/automatic", result);
    public AutomaticPlanningResult WithoutGeometry(AutomaticPlanningResult saved)
    {
      var plan = saved.State!.Plan!;
      return saved with { State = saved.State with { Plan = new() { Id = plan.Id, TruckId = plan.TruckId,
        DispatchId = plan.DispatchId, Version = plan.Version, GeometryOmitted = true,
        Route = new() { Legs = plan.Route.Legs.Select(x => x with { Points = [] }).ToList() } } } };
    }
    public JsonDocument LastNextPayload() => JsonDocument.Parse((byte[])Js.Calls.Last(x => x.Name == "setNextLoadsBytes").Args![0]!);
    public JsonDocument LastCurrentPayload() => JsonDocument.Parse((byte[])Js.Calls.Last(x => x.Name == "setRouteBytes").Args![0]!);
    public JsonElement LastLoadReference() => JsonSerializer.SerializeToElement(
      Js.Calls.Last(x => x.Name == "setLoadReference").Args![0], new JsonSerializerOptions(JsonSerializerDefaults.Web));
    public void AssertLoadReference(Guid dispatchId, int loadNumber, string orderNumber)
    {
      var reference = LastLoadReference();
      Assert.Equal(4, reference.EnumerateObject().Count());
      Assert.Equal(dispatchId, reference.GetProperty("dispatchId").GetGuid());
      Assert.Equal(loadNumber, reference.GetProperty("loadNumber").GetInt32());
      Assert.Equal(loadNumber.ToString(System.Globalization.CultureInfo.InvariantCulture), reference.GetProperty("loadLabel").GetString());
      Assert.Equal(orderNumber, reference.GetProperty("orderNumber").GetString());
    }
    public NextLoadRoute FutureLoad(int number)
    {
      var route = new NextLoadRoute(Guid.NewGuid(), number, "ready",
        [new(10, 600, [new(40, -80), new(41, -79)])], [new(40, -80, "Pickup") { Id = Guid.NewGuid() },
          new(41, -79, "Delivery") { Id = Guid.NewGuid() }]);
      _futureLoads[route.Id] = route;
      return route;
    }
    public NextLoadRoute CurrentLoad(Guid truck) => FutureLoad(1358) with { Id = _plans[truck].DispatchId!.Value };
    public void AssertIdentity(PendingNextRequest request, Guid truck, string revision)
    {
      Assert.Equal($"/api/dispatch/truck/{truck}/next-routes", request.Uri.AbsolutePath);
      Assert.Equal($"?currentDispatchId={_plans[truck].DispatchId}&revision={revision}", request.Uri.Query);
    }
    public void AssertOnlyFuturePublished(Guid id)
    {
      var call = Assert.Single(Js.Calls, x => x.Name == "setNextLoadsBytes");
      using var json = JsonDocument.Parse((byte[])call.Args![0]!);
      var route = Assert.Single(json.RootElement.GetProperty("routes").EnumerateArray());
      Assert.Equal(id, route.GetProperty("id").GetGuid());
    }
    private Task<HttpResponseMessage> RespondAsync(HttpRequestMessage request, CancellationToken ct)
    {
      Interlocked.Increment(ref HttpCalls);
      var uri = request.RequestUri!;
      if (uri.AbsolutePath == "/api/fuel/stations") return Task.FromResult(Ok(Array.Empty<object>()));
      if (uri.AbsolutePath.EndsWith("/planning/fuel/recalculate", StringComparison.Ordinal))
        return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
          { Content = JsonContent.Create(new { success = false, errors = new[] { "Fuel calculation unavailable." } }) });
      if (uri.AbsolutePath.EndsWith("/next-routes", StringComparison.Ordinal))
      {
        Interlocked.Increment(ref NextCalls);
        var pending = new PendingNextRequest(uri, ct);
        _next.Writer.TryWrite(pending);
        return pending.Response.Task.WaitAsync(_shutdown.Token);
      }
      if (uri.AbsolutePath == "/api/fleet/locations") return Task.FromResult(Ok(new
      {
        trucks = new[] { new { truckId = TruckA, unitNumber = "54777" }, new { truckId = TruckB, unitNumber = "64888" } },
        points = Array.Empty<object>()
      }));
      if (uri.AbsolutePath.EndsWith("/planning/preview", StringComparison.Ordinal))
      {
        Interlocked.Increment(ref PreviewCalls);
        if (PreviewUnavailable) return Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
        return DeferPreview ? Defer(_preview, uri, ct) : Task.FromResult(Ok(_plans[Guid.Parse(uri.Segments[^3].Trim('/'))]));
      }
      if (uri.AbsolutePath.EndsWith("/planning", StringComparison.Ordinal))
      {
        Interlocked.Increment(ref PlanningCalls);
        return DeferPlanning ? Defer(_planning, uri, ct) : Task.FromResult(Ok(_plans[Guid.Parse(uri.Segments[^2].Trim('/'))]));
      }
      if (_plans.Values.FirstOrDefault(x => uri.AbsolutePath == $"/api/dispatch/{x.DispatchId}/planning/automatic") is { } automatic)
      {
        Interlocked.Increment(ref PlanningCalls);
        return DeferPlanning ? Defer(_planning, uri, ct) : Task.FromResult(Ok(automatic));
      }
      if (_plans.Values.FirstOrDefault(x => uri.AbsolutePath == $"/api/dispatch/{x.DispatchId}") is { } plan)
      {
        Interlocked.Increment(ref DetailsCalls);
        return DeferDetails ? Defer(_details, uri, ct) : Task.FromResult(Ok(new { id = plan.DispatchId, loadNumber = plan.LoadNumber }));
      }
      if (_futureLoads.Values.FirstOrDefault(x => uri.AbsolutePath == $"/api/dispatch/{x.Id}") is { } future)
      {
        Interlocked.Increment(ref DetailsCalls);
        return DeferDetails ? Defer(_details, uri, ct) : Task.FromResult(Ok(new { id = future.Id, loadNumber = future.LoadNumber }));
      }
      if (uri.AbsolutePath.StartsWith("/api/dispatch/truck/", StringComparison.Ordinal)) Interlocked.Increment(ref DetailsCalls);
      return Task.FromResult(Ok(Array.Empty<object>()));
    }
    private Task<HttpResponseMessage> Defer(Channel<PendingHttpRequest> channel, Uri uri, CancellationToken ct)
    {
      var pending = new PendingHttpRequest(uri, ct);
      channel.Writer.TryWrite(pending);
      return pending.Response.Task.WaitAsync(_shutdown.Token);
    }
    public void Dispose()
    {
      _shutdown.Cancel();
      _context.Dispose();
      _shutdown.Dispose();
    }
  }

  private class PendingHttpRequest(Uri uri, CancellationToken cancellation)
  {
    public Uri Uri { get; } = uri;
    public CancellationToken Cancellation { get; } = cancellation;
    // A completed HTTP response can race cancellation; the component must still reject its old identity/version.
    public TaskCompletionSource<HttpResponseMessage> Response { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public void Reply(object response) => Response.SetResult(Ok(response));
  }

  private sealed class PendingNextRequest(Uri uri, CancellationToken cancellation) : PendingHttpRequest(uri, cancellation)
  {
    public void Reply(string revision, IReadOnlyList<NextLoadRoute> routes) =>
      Response.SetResult(Ok(new NextLoadRoutesResponse(revision, false, routes)));
    public void ReplyUnchanged(string revision) => Response.SetResult(Ok(new NextLoadRoutesResponse(revision, true, null)));
    public void ReplyLabels(string revision, IReadOnlyList<NextLoadLabels> labels) =>
      Response.SetResult(Ok(new NextLoadRoutesResponse(revision, false, null, labels)));
  }

  private static HttpResponseMessage Ok(object response) => new(HttpStatusCode.OK)
    { Content = JsonContent.Create(new { success = true, response }) };

  private sealed class Fixture : IDisposable
  {
    public Guid TruckId { get; } = Guid.NewGuid();
    private readonly Guid _dispatchId = Guid.NewGuid();
    public FakeTimeProvider Clock { get; } = new();
    public MapJs Js { get; } = new();
    public int NextCalls;
    public int StationCalls;
    public bool FailLocations;
    private readonly ClientComponentContext _context;
    public Fixture()
    {
      _context = new ClientComponentContext((request, _) => Task.FromResult(Respond(request.RequestUri!)));
      _context.Services.AddSingleton<TimeProvider>(Clock);
      _context.Services.AddSingleton<IJSRuntime>(Js);
      _context.Services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());
    }
    public IRenderedComponent<FleetMap> Render(Guid? truckId = null)
    {
      if (truckId is { } target) _context.Services.GetRequiredService<NavigationManager>()
        .NavigateTo($"/fleet/map?truckId={target}");
      return _context.Render<FleetMap>();
    }
    private HttpResponseMessage Respond(Uri uri)
    {
      if (uri.AbsolutePath == "/api/fuel/stations") { StationCalls++; return Ok(Array.Empty<object>()); }
      if (uri.AbsolutePath == "/api/fleet/locations") return FailLocations ? new(HttpStatusCode.ServiceUnavailable)
        : Ok(new { trucks = new[] { new { truckId = TruckId, unitNumber = "54777" } }, points = Array.Empty<object>() });
      if (uri.AbsolutePath.EndsWith("/planning")) return Ok(new { truckId = TruckId, dispatchId = _dispatchId, loadNumber = 1358 });
      if (uri.AbsolutePath.EndsWith("/next-routes"))
      {
        NextCalls++;
        return NextCalls == 2 ? new(HttpStatusCode.ServiceUnavailable)
          : Ok(new { revision = "saved", unchanged = NextCalls > 2, routes = NextCalls == 1 ? Array.Empty<object>() : null });
      }
      if (uri.AbsolutePath == $"/api/dispatch/{_dispatchId}") return Ok(new { id = _dispatchId, loadNumber = 1358 });
      return Ok(Array.Empty<object>());
    }
    public void Dispose() => _context.Dispose();
  }

  private sealed class MapJs : IJSRuntime, IJSObjectReference
  {
    public ConcurrentQueue<(string Name, object?[]? Args)> Calls { get; } = new();
    private readonly Channel<byte[]> _currentPayloads = Channel.CreateUnbounded<byte[]>();
    private readonly Channel<byte[]> _nextPayloads = Channel.CreateUnbounded<byte[]>();
    public Task<byte[]> ReadCurrentPayloadAsync() => _currentPayloads.Reader.ReadAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));
    public Task<byte[]> ReadNextPayloadAsync() => _nextPayloads.Reader.ReadAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));
    public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args) => InvokeAsync<TValue>(identifier, default, args);
    public ValueTask<TValue> InvokeAsync<TValue>(string identifier, CancellationToken ct, object?[]? args)
    {
      ct.ThrowIfCancellationRequested();
      Calls.Enqueue((identifier, args));
      if (identifier == "setRouteBytes") _currentPayloads.Writer.TryWrite((byte[])args![0]!);
      if (identifier == "setNextLoadsBytes") _nextPayloads.Writer.TryWrite((byte[])args![0]!);
      object? result = typeof(TValue) == typeof(IJSObjectReference) ? this : typeof(TValue) == typeof(bool) ? true : default(TValue);
      return ValueTask.FromResult((TValue)result!);
    }
    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
  }
}
