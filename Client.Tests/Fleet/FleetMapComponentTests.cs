using System.Collections.Concurrent;
using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Channels;
using AngleSharp.Dom;
using Bunit;
using Client.Models;
using Client.Models.DTO.Dispatch;
using Client.Models.DTO.Fleet;
using Client.Models.DTO.Planning;
using Client.Pages.FleetMap;
using Client.Services;
using Client.Shared.DriverStatus.ArrivalEstimate;
using Client.Shared.Fuel.FuelPlanEditor;
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
  private static void AssertTruckPanelsVisible(
    IRenderedComponent<FleetMap> component
  )
  {
    Assert.False(component.Find("#fleet-map-details").HasAttribute("hidden"));
    var telemetry = component.Find("#fleet-map-telemetry-details");
    var route = component.Find("#fleet-map-route-details");
    Assert.False(telemetry.HasAttribute("hidden"));
    Assert.False(route.HasAttribute("hidden"));
    // Route first: what the dispatcher decides on. Telematics follows it in
    // the document, not only on screen, so reading order matches.
    Assert.Equal(telemetry.Id, route.NextElementSibling!.Id);
    Assert.Single(component.FindAll(".fleet-map-mobile-summary__toggle"));
    Assert.Empty(component.FindAll(".fleet-map-truck-info__more"));
  }

  [Fact]
  public async Task PhoneTruckDetailsStartCollapsedAndToggleWithoutReloading()
  {
    using var fixture = new SelectionFixture();
    var component = fixture.Render();
    component.WaitForAssertion(
      () => Assert.Contains(fixture.Js.Calls, call => call.Name == "setTrucks")
    );

    await component.InvokeAsync(
      () => component.Instance.OnTruckSelected(fixture.TruckA.ToString())
    );

    var inspector = component.Find(".fleet-map-inspector");
    var toggle = component.Find(".fleet-map-mobile-summary__toggle");
    Assert.Contains("is-mobile-collapsed", inspector.ClassList);
    Assert.Equal("false", toggle.GetAttribute("aria-expanded"));
    var closed = component.Find(".fleet-map-inspector__header").InnerHtml;

    await toggle.ClickAsync(new MouseEventArgs());

    Assert.Contains("is-mobile-expanded", inspector.ClassList);
    Assert.Equal("true", toggle.GetAttribute("aria-expanded"));
    // The header is what stays on screen either way, so opening the card may
    // not rewrite a word of it - a "Details"/"Hide" toggle used to, and the
    // row shifted under the dispatcher's finger as it opened.
    Assert.Equal(
      closed.Replace("aria-expanded=\"false\"", "aria-expanded=\"true\""),
      component.Find(".fleet-map-inspector__header").InnerHtml
    );
    Assert.Equal("Truck details", toggle.GetAttribute("aria-label"));
  }

  [Theory]
  [InlineData("truck")]
  [InlineData("stop")]
  [InlineData("fuel")]
  public async Task FuelEditorHidesPreviousInspectorAndRejectsLateMarkerUpdates(
    string mode
  )
  {
    using var fixture = new SelectionFixture();
    var component = fixture.Render();
    component.WaitForAssertion(
      () => Assert.Contains(fixture.Js.Calls, call => call.Name == "setTrucks")
    );
    await component.InvokeAsync(
      () => component.Instance.OnTruckSelected(fixture.TruckA.ToString())
    );
    await component.InvokeAsync(
      () =>
        component.Instance.OnMapInspectorChanged(
          mode,
          fixture.TruckA.ToString(),
          10
        )
    );
    await component.InvokeAsync(
      () =>
        component.Instance.OnFuelStationEdit(
          fixture.TruckA.ToString(),
          fixture.Plan(fixture.TruckA).DispatchId!.Value.ToString(),
          Guid.NewGuid().ToString(),
          "Station",
          null,
          false
        )
    );
    var editor = component.FindComponent<FuelPlanEditor>().Instance;
    Assert.False(
      component.Find(".fleet-map-inspector").ClassList.Contains("has-selection")
    );
    Assert.Equal(
      true,
      fixture
        .Js.Calls.Last(call => call.Name == "setInspectionSuspended")
        .Args![0]
    );
    var revision = 20L;
    foreach (var lateMode in new[] { "truck", "stop", "fuel", "next-stop" })
      await component.InvokeAsync(
        () =>
          component.Instance.OnMapInspectorChanged(
            lateMode,
            fixture.TruckA.ToString(),
            revision++
          )
      );
    Assert.Same(editor, component.FindComponent<FuelPlanEditor>().Instance);
    Assert.False(
      component.Find(".fleet-map-inspector").ClassList.Contains("has-selection")
    );
    await component.InvokeAsync(() => editor.Closed.InvokeAsync());
    var cameraReturn = fixture.Js.Calls.Last(call =>
      call.Name == "clearFuelStationFocus"
    );
    Assert.Equal(true, cameraReturn.Args![0]);
    var routeIdentity = JsonSerializer.SerializeToElement(cameraReturn.Args[1]);
    Assert.Equal(
      fixture.TruckA,
      routeIdentity.GetProperty("TruckId").GetGuid()
    );
    Assert.Equal(
      fixture.Plan(fixture.TruckA).DispatchId,
      routeIdentity.GetProperty("DispatchId").GetGuid()
    );
    component.WaitForAssertion(
      () =>
        Assert.True(
          component
            .Find(".fleet-map-inspector")
            .ClassList.Contains("has-selection")
        )
    );
    Assert.Equal(
      "truck",
      component.Find(".fleet-map-inspector").GetAttribute("data-inspector-mode")
    );
    Assert.Equal(
      false,
      fixture
        .Js.Calls.Last(call => call.Name == "setInspectionSuspended")
        .Args![0]
    );
    Assert.Equal(0, fixture.FuelWrites);
  }

  [Theory]
  [InlineData("Camera", "Close camera", ".truck-camera")]
  [InlineData("Route options", "Close route editor", ".route-editor")]
  public async Task CameraAndRouteEditorReplaceTheInspectorAndRestoreItOnClose(
    string action,
    string close,
    string selector
  )
  {
    using var fixture = new SelectionFixture();
    var component = fixture.Render();
    component.WaitForAssertion(
      () => Assert.Contains(fixture.Js.Calls, call => call.Name == "setTrucks")
    );
    await component.InvokeAsync(
      () => component.Instance.OnTruckSelected(fixture.TruckA.ToString())
    );
    await component
      .Find($"button[aria-label='{action}']")
      .ClickAsync(new MouseEventArgs());
    Assert.Single(component.FindAll(selector));
    Assert.Null(component.Find(selector).Closest(".fleet-map-inspector"));
    Assert.False(
      component.Find(".fleet-map-inspector").ClassList.Contains("has-selection")
    );
    await component.InvokeAsync(
      () =>
        component.Instance.OnMapInspectorChanged(
          "fuel",
          fixture.TruckA.ToString(),
          100
        )
    );
    Assert.False(
      component.Find(".fleet-map-inspector").ClassList.Contains("has-selection")
    );
    await component
      .Find($"button[aria-label='{close}']")
      .ClickAsync(new MouseEventArgs());
    component.WaitForAssertion(
      () =>
        Assert.True(
          component
            .Find(".fleet-map-inspector")
            .ClassList.Contains("has-selection")
        )
    );
    Assert.Empty(component.FindAll(selector));
    Assert.Equal(0, fixture.FuelWrites);
  }

  [Fact]
  public async Task ToolbarCarriesOnlyLayersAndDisclosureRetainsMapAndInputs()
  {
    using var fixture = new SelectionFixture();
    var component = fixture.Render();
    component.WaitForAssertion(
      () => Assert.Contains(fixture.Js.Calls, call => call.Name == "setTrucks")
    );
    var map = component.Find("#fleet-map");
    var search = component.Find("#fleet-truck-search");
    var layers = component.Find("[aria-label='Map layers']");
    Assert.Equal(3, layers.QuerySelectorAll("input[type='checkbox']").Length);
    Assert.Equal(3, layers.QuerySelectorAll("svg[aria-hidden='true']").Length);
    // Neither the date nor the price basis is a map question: the map is
    // today, and it prices fuel the way the planner does.
    Assert.Empty(component.FindAll("#fleet-date"));
    Assert.Empty(component.FindAll("[aria-label='Fuel price mode']"));
    var filters = component.Find(".fleet-map-mobile-filters");
    Assert.Equal("false", filters.GetAttribute("aria-expanded"));
    await filters.ClickAsync(new MouseEventArgs());
    Assert.Equal("true", filters.GetAttribute("aria-expanded"));
    Assert.Contains("is-open", component.Find("#fleet-map-filters").ClassList);
    await filters.ClickAsync(new MouseEventArgs());
    Assert.Equal("false", filters.GetAttribute("aria-expanded"));
    Assert.Equal(map.OuterHtml, component.Find("#fleet-map").OuterHtml);
    Assert.Equal(
      search.OuterHtml,
      component.Find("#fleet-truck-search").OuterHtml
    );
    Assert.Equal(0, fixture.FuelWrites);
  }

  [Fact]
  public async Task HosStartsOnMapOpenAndRemainsVisibleWhileColdPreviewAndLiveRouteArePending()
  {
    using var fixture = new SelectionFixture(cacheCurrentPlans: false)
    {
      DeferPreview = true,
      DeferPlanning = true,
    };
    fixture.Hos = new Dictionary<Guid, TruckHosSnapshot>
    {
      [fixture.TruckA] = new(
        "Current driver",
        new()
        {
          UpdatedAt = fixture.Clock.GetUtcNow().UtcDateTime,
          DriveMs = 7200000,
        }
      ),
    };
    var component = fixture.Render();
    component.WaitForAssertion(() => Assert.Equal(1, fixture.HosCalls));
    component.WaitForAssertion(
      () => Assert.Contains(fixture.Js.Calls, call => call.Name == "setTrucks")
    );
    var selection = component.InvokeAsync(
      () => component.Instance.OnTruckSelected(fixture.TruckA.ToString())
    );
    var preview = await fixture.ReadPreviewAsync();
    component.WaitForAssertion(
      () =>
        Assert.Contains(
          "—",
          component
            .Find(".fleet-map-mobile-summary__remaining strong")
            .TextContent
        )
    );
    component.WaitForAssertion(
      () => Assert.Contains("2:00", component.Find(".driver-hours").TextContent)
    );
    Assert.Contains(
      "Current driver",
      component.Find(".fleet-map-inspector__driver").TextContent
    );
    preview.Reply(
      fixture.Plan(fixture.TruckA) with
      {
        Hos = new()
        {
          UpdatedAt = fixture.Clock.GetUtcNow().UtcDateTime,
          DriveMs = 3600000,
        },
      }
    );
    var planning = await fixture.ReadPlanningAsync();
    Assert.Contains("2:00", component.Find(".driver-hours").TextContent);
    planning.Reply(fixture.Plan(fixture.TruckA));
    await selection;
    Assert.Contains("2:00", component.Find(".driver-hours").TextContent);
  }

  [Fact]
  public async Task StoppingFollowRetainsTruckAndVisibleReadings()
  {
    using var fixture = new SelectionFixture();
    var component = fixture.Render();
    component.WaitForAssertion(
      () => Assert.Contains(fixture.Js.Calls, call => call.Name == "setTrucks")
    );
    await component.InvokeAsync(
      () => component.Instance.OnTruckSelected(fixture.TruckA.ToString())
    );
    await component.InvokeAsync(() => component.Instance.OnFollowChanged(true));
    var truck = component.Find(".fleet-map-truck-info").TextContent;
    var route = component.Find(".fleet-map-route-info").TextContent;
    var requests = fixture.HttpCalls;
    var interop = fixture.Js.Calls.Count;

    await component.InvokeAsync(
      () => component.Instance.OnFollowChanged(false)
    );

    Assert.True(
      component
        .Find(".fleet-map-info-reserved")
        .ClassList.Contains("has-selection")
    );
    Assert.Equal(truck, component.Find(".fleet-map-truck-info").TextContent);
    Assert.Equal(route, component.Find(".fleet-map-route-info").TextContent);
    Assert.False(
      component.Find("#fleet-map-telemetry-details").HasAttribute("hidden")
    );
    Assert.Equal(requests, fixture.HttpCalls);
    Assert.Equal(interop, fixture.Js.Calls.Count);
    Assert.Equal(0, fixture.FuelWrites);
  }

  [Theory]
  [InlineData(false)]
  [InlineData(true)]
  public async Task CompactDistanceDeduplicationUsesTrackedFinalStopIdentity(
    bool final
  )
  {
    using var fixture = new SelectionFixture();
    var plan = fixture.Plan(fixture.TruckA).State!.Plan!;
    var pickup = new PlanStop(
      Guid.NewGuid(),
      "Pickup",
      "Webster, NY",
      1,
      new(43, -77)
    )
    {
      Job = "Pick Up",
    };
    var delivery = new PlanStop(
      Guid.NewGuid(),
      "Delivery",
      "Cowpens, SC",
      2,
      new(35, -82)
    )
    {
      Job = "Drop Off",
    };
    plan.Stops = [pickup, delivery];
    plan.Tracking.NextStopId = final ? delivery.Id : pickup.Id;
    var component = fixture.Render();
    component.WaitForAssertion(
      () => Assert.Contains(fixture.Js.Calls, call => call.Name == "setTrucks")
    );
    await component.InvokeAsync(
      () => component.Instance.OnTruckSelected(fixture.TruckA.ToString())
    );
    var route = component.Find("[aria-label='Current dispatch route']");
    Assert.Equal(final, route.ClassList.Contains("has-final-stop"));
    Assert.Single(
      route.QuerySelectorAll(
        ".fleet-map-route-info__distances > .fleet-map-route-info__metric"
      )
    );
    Assert.Empty(
      route.QuerySelectorAll(
        ".fleet-map-route-info__distances .fleet-map-route-info__distance"
      )
    );
    var heading = component.Find(".fleet-map-route-info__visit-heading");
    Assert.Equal(
      final ? "Drop Off" : "Pick Up",
      heading.QuerySelector(".fleet-map-route-info__label")!.TextContent
    );
    Assert.NotNull(
      heading.QuerySelector(
        ".fleet-map-route-info__distance[title='Distance to next stop']"
      )
    );
    Assert.Contains(
      "km",
      heading.QuerySelector(".fleet-map-route-info__distance")!.TextContent
    );
    // The card's second line leads with the load being run and what is
    // left of it, then the clocks that say whether it can be finished.
    var second = component.Find(".fleet-map-inspector__hours");
    Assert.Equal(
      [
        "fleet-map-mobile-summary__remaining",
        "fleet-map-inspector__hours-label",
        "driver-hours-panel",
      ],
      second.Children.Select(node => node.ClassName)
    );
    Assert.Equal(
      "mi left",
      component.FindAll(".fleet-map-mobile-summary__label")[^1].TextContent
    );
    Assert.All(
      route.QuerySelectorAll(".fleet-map-route-info__metric"),
      metric =>
        Assert.Contains(
          "km",
          metric.QuerySelector(".fleet-map-route-info__secondary")!.TextContent
        )
    );
    Assert.Single(
      component.FindAll(
        ".fleet-map-inspector__hours [aria-label='Driver hours remaining']"
      )
    );
    Assert.Equal(0, fixture.FuelWrites);
  }

  [Theory]
  [InlineData("Pick Up", "Pickup")]
  [InlineData("Drop Off", "Delivery")]
  [InlineData("Delivery", "Delivery")]
  public async Task CompactSummaryShowsOnlyTheTrackedStopsAppointment(
    string job,
    string label
  )
  {
    using var fixture = new SelectionFixture { DeferDetails = true };
    var plan = fixture.Plan(fixture.TruckA).State!.Plan!;
    var next = new PlanStop(
      Guid.NewGuid(),
      "Current facility",
      "Webster, NY",
      1,
      new(43, -77)
    )
    {
      Job = job,
    };
    plan.Stops = [next];
    plan.Tracking.NextStopId = next.Id;
    var component = fixture.Render();
    component.WaitForAssertion(
      () => Assert.Contains(fixture.Js.Calls, call => call.Name == "setTrucks")
    );
    var selection = component.InvokeAsync(
      () => component.Instance.OnTruckSelected(fixture.TruckA.ToString())
    );
    (await fixture.ReadDetailsAsync()).Reply(
      new DispatchResponse
      {
        Id = fixture.Plan(fixture.TruckA).DispatchId!.Value,
        LoadNumber = 1376,
        OrderNumber = "ORDER-5678",
        Stops =
        [
          new()
          {
            Id = Guid.NewGuid(),
            Sequence = 3,
            Job = "Drop Off",
            ScheduledDate = new(2026, 9, 15),
            ScheduledTime = new(8, 0),
            ScheduledTime2 = new(10, 0),
          },
          new()
          {
            Id = next.Id,
            Sequence = 1,
            Job = job,
            ScheduledDate = new(2026, 9, 12),
            ScheduledTime = new(13, 0),
          },
          new()
          {
            Sequence = 4,
            Job = "Driver start",
            DriverOnly = true,
            ScheduledDate = new(2026, 9, 16),
          },
        ],
      }
    );
    await selection;
    var summary = component.Find(".fleet-map-route-info__load");
    Assert.Contains("1376", summary.TextContent);
    Assert.Contains("ORDER-5678", summary.TextContent);
    Assert.Equal(
      "ORDER-5678",
      summary
        .QuerySelector(
          "[title='Copy order number'] strong.fleet-map-inspector__value"
        )!
        .TextContent
    );
    Assert.NotNull(
      summary.QuerySelector(
        ".fleet-map-route-info__total > strong.fleet-map-inspector__value"
      )
    );
    await component
      .Find("[title='Copy order number']")
      .ClickAsync(new MouseEventArgs());
    Assert.Equal(
      "ORDER-5678",
      fixture
        .Js.Calls.Last(x => x.Name == "navigator.clipboard.writeText")
        .Args![0]
    );
    Assert.Empty(summary.QuerySelectorAll(".fleet-map-route-info__delivery"));
    var appointment = Assert.Single(
      component.FindAll(
        ".fleet-map-route-info__timing > .fleet-map-route-info__appointment"
      )
    );
    Assert.Equal(
      "Sep 12 · 01:00 PM",
      appointment.QuerySelector("strong")!.TextContent.Replace('\u00a0', ' ')
    );
    Assert.Equal(
      label,
      appointment.QuerySelector(".fleet-map-route-info__label")!.TextContent
    );
    Assert.DoesNotContain(
      "Sep 15",
      component.Find(".fleet-map-route-info__timing").TextContent
    );
    AssertTruckPanelsVisible(component);
    Assert.Single(
      component.FindAll(
        ".fleet-map-inspector__hours [aria-label='Driver hours remaining']"
      )
    );
    fixture.DeferDetails = false;
    await component.InvokeAsync(
      () => component.Instance.OnTruckSelected(fixture.TruckB.ToString())
    );
    Assert.DoesNotContain("ORDER-5678", component.Markup);
    Assert.Equal(0, fixture.FuelWrites);
  }

  [Fact]
  public async Task CompactSummaryAlwaysShowsTheTrackedStopsFuelOnArrival()
  {
    using var fixture = new SelectionFixture();
    var result = fixture.Plan(fixture.TruckA);
    var plan = result.State!.Plan!;
    var stop = new PlanStop(
      Guid.NewGuid(),
      "Current facility",
      "Webster, NY",
      1,
      new(43, -77)
    )
    {
      Job = "Pick Up",
    };
    plan.Stops = [stop];
    plan.Tracking.NextStopId = stop.Id;
    result.State.FuelStopArrivals =
    [
      new(plan.DispatchId, stop.Id, 126.4, 50.56),
      new(Guid.NewGuid(), stop.Id, 5, 2),
      new(plan.DispatchId, Guid.NewGuid(), 10, 4),
    ];
    var component = fixture.Render();
    component.WaitForAssertion(
      () => Assert.Contains(fixture.Js.Calls, call => call.Name == "setTrucks")
    );

    await component.InvokeAsync(
      () => component.Instance.OnTruckSelected(fixture.TruckA.ToString())
    );

    var fuel = component.Find(
      "[aria-label='Current dispatch route'] "
        + ".fleet-map-route-info__arrival-fuel"
    );
    Assert.Equal("Fuel on arrival", fuel.QuerySelector("span")!.TextContent);
    Assert.Equal("51%", fuel.QuerySelector("strong")!.TextContent);
    Assert.Contains("126 US gal", fuel.TextContent);

    result.State.FuelStopArrivals = [];
    fixture.SetPlanningResult(fixture.TruckA, result);
    await component.InvokeAsync(
      () => component.Instance.OnTruckSelected(fixture.TruckB.ToString())
    );
    await component.InvokeAsync(
      () => component.Instance.OnTruckSelected(fixture.TruckA.ToString())
    );
    Assert.Equal(
      "—",
      component.Find(".fleet-map-route-info__arrival-fuel strong").TextContent
    );
  }

  [Fact]
  public async Task SelectedTruckLocationUsesTelemetryRefreshAndNeverAnotherTruckOrRouteAddress()
  {
    using var fixture = new SelectionFixture
    {
      AddressA = "1886 Tebor Rd, Webster, NY",
    };
    var component = fixture.Render();
    component.WaitForAssertion(
      () => Assert.Contains(fixture.Js.Calls, call => call.Name == "setTrucks")
    );
    await component.InvokeAsync(
      () => component.Instance.OnTruckSelected(fixture.TruckA.ToString())
    );
    var location = component.Find("[aria-label='Truck GPS location']");
    Assert.Contains(fixture.AddressA, location.TextContent);
    Assert.Contains("Current location", location.TextContent);
    await component
      .Find("[title='Copy location address']")
      .ClickAsync(new MouseEventArgs());
    Assert.Equal(
      fixture.AddressA,
      fixture
        .Js.Calls.Last(x => x.Name == "navigator.clipboard.writeText")
        .Args![0]
    );
    Assert.Null(location.QuerySelector("time"));
    Assert.DoesNotContain("GPS", location.TextContent);
    AssertTruckPanelsVisible(component);
    Assert.Contains(
      fixture.AddressA,
      component.Find("[aria-label='Truck GPS location']").TextContent
    );
    await component
      .Find("[title='Copy location address']")
      .ClickAsync(new MouseEventArgs());
    Assert.Equal(
      fixture.AddressA,
      fixture
        .Js.Calls.Last(x => x.Name == "navigator.clipboard.writeText")
        .Args![0]
    );
    fixture.AddressA = "1730 NY-5S, Amsterdam, NY";
    await component.InvokeAsync(
      () => fixture.Clock.Advance(TimeSpan.FromSeconds(10))
    );
    component.WaitForAssertion(
      () =>
        Assert.Contains(
          fixture.AddressA,
          component.Find("[aria-label='Truck GPS location']").TextContent
        )
    );
    Assert.DoesNotContain(
      "Webster",
      component.Find("[aria-label='Truck GPS location']").TextContent
    );
    await component
      .Find("[title='Copy location address']")
      .ClickAsync(new MouseEventArgs());
    Assert.Equal(
      fixture.AddressA,
      fixture
        .Js.Calls.Last(x => x.Name == "navigator.clipboard.writeText")
        .Args![0]
    );
    await component.InvokeAsync(
      () => component.Instance.OnTruckSelected(fixture.TruckB.ToString())
    );
    Assert.Contains(
      "Address unavailable",
      component.Find("[aria-label='Truck GPS location']").TextContent
    );
    Assert.DoesNotContain(
      "Amsterdam",
      component.Find("[aria-label='Truck GPS location']").TextContent
    );
    Assert.Empty(component.FindAll("[aria-label='Truck GPS location'] time"));
    Assert.Empty(component.FindAll("[title='Copy location address']"));
    Assert.Equal(0, fixture.FuelWrites);
  }

  [Fact]
  public async Task ChangingDisplayUnitsUpdatesTheInspectorWithoutRefetchingOrReplacingTheRoad()
  {
    using var fixture = new SelectionFixture { OutsideA = 26.5m };
    var plan = fixture.Plan(fixture.TruckA).State!.Plan!;
    var stop = new PlanStop(Guid.NewGuid(), "Next", "", 1, new(40, -80));
    plan.Stops = [stop];
    plan.FromCurrentPosition = true;
    plan.Tracking.NextStopId = stop.Id;
    plan.Route.Legs[0] = plan.Route.Legs[0] with { Miles = 10 };

    var wrapper = fixture.RenderWithUnits();
    var component = wrapper.FindComponent<FleetMap>();
    component.WaitForAssertion(
      () => Assert.Contains(fixture.Js.Calls, call => call.Name == "setTrucks")
    );
    await component.InvokeAsync(
      () => component.Instance.OnTruckSelected(fixture.TruckA.ToString())
    );
    var calls = fixture.HttpCalls;
    var routes = fixture.Js.Calls.Count(call => call.Name == "setRouteBytes");
    await component.InvokeAsync(
      () => component.Instance.OnRouteProgress(fixture.TruckA.ToString(), 10, 0)
    );
    wrapper.Render(parameters =>
      parameters
        .Add(
          cascade => cascade.Value,
          new DisplayUnits("celsius", "kilometers")
        )
        .AddChildContent<FleetMap>()
    );
    Assert.Contains(
      "26.5 °C",
      component.Find(".fleet-map-truck-info__outside").TextContent
    );
    Assert.DoesNotContain(
      "°F",
      component.Find(".fleet-map-truck-info__outside").TextContent
    );
    Assert.Contains(
      "16",
      component.Find(".fleet-map-route-info__metric strong").TextContent
    );
    Assert.Contains(
      "km",
      component.Find(".fleet-map-route-info__metric strong").TextContent
    );
    Assert.Equal(
      "10",
      component.Find(".fleet-map-mobile-summary__remaining strong").TextContent
    );
    Assert.Empty(
      component.FindAll(
        ".fleet-map-route-info__metric .fleet-map-route-info__secondary"
      )
    );
    Assert.Contains(
      fixture.Js.Calls,
      call =>
        call.Name == "setDistanceUnit"
        && call.Args?[0]?.ToString() == "kilometers"
    );
    wrapper.Render(parameters =>
      parameters
        .Add(cascade => cascade.Value, new DisplayUnits("fahrenheit", "miles"))
        .AddChildContent<FleetMap>()
    );
    Assert.Contains(
      "79.7 °F",
      component.Find(".fleet-map-truck-info__outside").TextContent
    );
    Assert.DoesNotContain(
      "°C",
      component.Find(".fleet-map-truck-info__outside").TextContent
    );
    wrapper.Render(parameters =>
      parameters
        .Add(cascade => cascade.Value, DisplayUnits.Default)
        .AddChildContent<FleetMap>()
    );
    Assert.Equal(
      "10",
      component.Find(".fleet-map-mobile-summary__remaining strong").TextContent
    );
    Assert.Equal(
      "16 km",
      component
        .Find(".fleet-map-route-info__metric .fleet-map-route-info__secondary")
        .TextContent
    );
    Assert.Equal(calls, fixture.HttpCalls);
    Assert.Equal(
      routes,
      fixture.Js.Calls.Count(call => call.Name == "setRouteBytes")
    );
    Assert.Equal(0, fixture.FuelWrites);
  }

  [Fact]
  public async Task OutsideTemperatureKeepsZeroConvertsUnitsAndClearsWhenChangingTruck()
  {
    using var fixture = new SelectionFixture { OutsideA = 0 };
    var component = fixture.Render();
    component.WaitForAssertion(
      () => Assert.Contains(fixture.Js.Calls, call => call.Name == "setTrucks")
    );
    await component.InvokeAsync(
      () => component.Instance.OnTruckSelected(fixture.TruckA.ToString())
    );
    var outside = component.Find(
      ".fleet-map-truck-info__telemetry > .fleet-map-truck-info__outside"
    );
    Assert.Equal("Outside temperature", outside.GetAttribute("aria-label"));
    Assert.Contains("0 °C", outside.TextContent);
    Assert.DoesNotContain("°F", outside.TextContent);
    Assert.NotNull(outside.QuerySelector("svg"));
    Assert.Contains("Google Weather", outside.GetAttribute("title"));
    fixture.OutsideA = -40;
    await component.InvokeAsync(
      () => fixture.Clock.Advance(TimeSpan.FromMinutes(10))
    );
    component.WaitForAssertion(
      () =>
        Assert.Contains(
          "-40 °C",
          component.Find(".fleet-map-truck-info__outside").TextContent
        )
    );
    Assert.DoesNotContain(
      "°F",
      component.Find(".fleet-map-truck-info__outside").TextContent
    );
    await component.InvokeAsync(
      () => component.Instance.OnTruckSelected(fixture.TruckB.ToString())
    );
    outside = component.Find(".fleet-map-truck-info__outside");
    Assert.Contains("—", outside.TextContent);
    Assert.DoesNotContain("°F", outside.TextContent);
    Assert.DoesNotContain("°C", outside.TextContent);
    Assert.Equal(0, fixture.FuelWrites);
  }

  [Fact]
  public async Task TruckSelectionKeepsCameraAndShowRouteFitsWithoutRequests()
  {
    using var fixture = new SelectionFixture();
    var component = fixture.Render();
    component.WaitForAssertion(
      () => Assert.Contains(fixture.Js.Calls, call => call.Name == "setTrucks")
    );
    await component.InvokeAsync(
      () => component.Instance.OnTruckSelected(fixture.TruckA.ToString())
    );
    Assert.DoesNotContain(
      fixture.Js.Calls,
      call => call.Name == "setRouteBytes" && call.Args?[2] is true
    );
    var showRoute = component.Find("button[aria-label='Show route']");
    Assert.False(showRoute.HasAttribute("disabled"));
    Assert.Equal(
      "Follow",
      showRoute.PreviousElementSibling!.GetAttribute("aria-label")
    );
    var routeRequests = fixture.HttpCalls;
    var publications = fixture.Js.Calls.Count(c => c.Name == "setRouteBytes");
    await showRoute.ClickAsync(new MouseEventArgs());
    Assert.Contains(
      fixture.Js.Calls,
      c =>
        c.Name == "showRoute" && Equals(c.Args![0], fixture.TruckA.ToString())
    );
    Assert.Equal(routeRequests, fixture.HttpCalls);
    Assert.Equal(
      publications,
      fixture.Js.Calls.Count(c => c.Name == "setRouteBytes")
    );
    AssertTruckPanelsVisible(component);
    var summary = component.Find("#fleet-map-route-details");
    Assert.False(summary.HasAttribute("hidden"));
    Assert.Equal("fleet-map-telemetry-details", summary.NextElementSibling!.Id);
    Assert.NotNull(
      component.Find(
        ".fleet-map-inspector__identity .fleet-map-inspector__trailer"
      )
    );
    // The clocks read from the header; "hours are enough" and the recap are
    // not on the card at all.
    Assert.Single(
      component.FindAll(
        ".fleet-map-inspector__header > .fleet-map-inspector__hours"
      )
    );
    Assert.Empty(component.FindAll(".driver-duty"));
    Assert.Empty(component.FindAll(".driver-next-recap"));
    var loadLink = component.Find(
      ".fleet-map-inspector__header a[aria-label='Route & load details']"
    );
    Assert.Equal("Route & load details", loadLink.GetAttribute("title"));
    Assert.Single(loadLink.QuerySelectorAll("svg"));
    Assert.True(string.IsNullOrWhiteSpace(loadLink.TextContent));
    Assert.Equal(
      $"/dispatch/{fixture.Plan(fixture.TruckA).DispatchId}",
      loadLink.GetAttribute("href")
    );
    Assert.Empty(component.FindAll(".fleet-map-truck-info__details"));
    Assert.Single(component.FindAll(".fleet-map-inspector__title"));
    await component.InvokeAsync(
      () => component.Instance.OnTruckSelected(fixture.TruckB.ToString())
    );
    AssertTruckPanelsVisible(component);
    Assert.Equal(0, fixture.FuelWrites);
  }

  [Theory]
  [InlineData(false)]
  [InlineData(true)]
  public async Task DeliveryEnrichesOnlyAnAppointmentMissingFromPreview(
    bool previewHasAppointment
  )
  {
    using var fixture = new SelectionFixture(cacheCurrentPlans: false)
    {
      DeferPreview = true,
      DeferDetails = true,
      DeferPlanning = true,
    };
    var preview = JsonSerializer.Deserialize<AutomaticPlanningResult>(
      JsonSerializer.Serialize(fixture.Plan(fixture.TruckA))
    )!;
    var stop = new PlanStop(
      Guid.NewGuid(),
      "Receiving facility",
      "Cowpens, SC",
      1,
      new(35, -82)
    )
    {
      Job = "Drop Off",
      ScheduledDate = previewHasAppointment ? new(2026, 9, 14) : null,
      ScheduledTime = previewHasAppointment ? new(5, 0) : null,
    };
    preview.State!.Plan!.Stops = [stop];
    preview.State.Plan.Tracking.NextStopId = stop.Id;
    var component = fixture.Render();
    component.WaitForAssertion(
      () => Assert.Contains(fixture.Js.Calls, call => call.Name == "setTrucks")
    );
    var selection = component.InvokeAsync(
      () => component.Instance.OnTruckSelected(fixture.TruckA.ToString())
    );
    var pendingPreview = await fixture.ReadPreviewAsync();
    component.WaitForAssertion(() => AssertDelivery("—"));
    pendingPreview.Reply(preview);
    var pendingDetails = await fixture.ReadDetailsAsync();
    var pendingPlanning = await fixture.ReadPlanningAsync();
    component.WaitForAssertion(
      () => AssertDelivery(previewHasAppointment ? "Sep 14 · 05:00 AM" : "—")
    );
    pendingDetails.Reply(
      new DispatchResponse
      {
        Id = preview.DispatchId!.Value,
        LoadNumber = 1379,
        Stops =
        [
          new()
          {
            Id = stop.Id,
            Sequence = 1,
            Job = "Drop Off",
            Address = stop.Address,
            ScheduledDate = new(2026, 9, 13),
            ScheduledTime = new(13, 0),
          },
        ],
      }
    );
    component.WaitForAssertion(
      () =>
        AssertDelivery(
          previewHasAppointment ? "Sep 14 · 05:00 AM" : "Sep 13 · 01:00 PM"
        )
    );
    var live = JsonSerializer.Deserialize<AutomaticPlanningResult>(
      JsonSerializer.Serialize(preview)
    )!;
    live.State!.Plan!.Stops =
    [
      stop with
      {
        ScheduledDate = new(2026, 9, 13),
        ScheduledTime = new(13, 0),
      },
    ];
    pendingPlanning.Reply(live);
    await selection;
    component.WaitForAssertion(() => AssertDelivery("Sep 13 · 01:00 PM"));
    Assert.Equal(0, fixture.FuelWrites);

    void AssertDelivery(string expected)
    {
      var row = Assert.Single(
        component.FindAll(".fleet-map-route-info__delivery")
      );
      Assert.Contains(
        "fleet-map-route-info__timing",
        row.ParentElement!.ClassList
      );
      Assert.Equal(
        expected,
        row.QuerySelector("strong")!.TextContent.Replace('\u00a0', ' ')
      );
      Assert.Empty(
        component.FindAll(".fleet-map-route-info__next-appointment")
      );
      Assert.Empty(
        component.FindAll(
          ".fleet-map-route-info__load .fleet-map-route-info__delivery"
        )
      );
    }
  }

  [Fact]
  public async Task PhoneRemainingSummaryUpdatesWithProgressAndStaysAvailableOnBackgroundClicks()
  {
    using var fixture = new SelectionFixture();
    var plan = fixture.Plan(fixture.TruckA).State!.Plan!;
    var stop = new PlanStop(Guid.NewGuid(), "Next", "", 1, new(40, -80));
    plan.Stops = [stop];
    plan.FromCurrentPosition = true;
    plan.Tracking.NextStopId = stop.Id;
    plan.Route.Legs[0] = plan.Route.Legs[0] with { Miles = 30 };

    var component = fixture.Render();
    component.WaitForAssertion(
      () => Assert.Contains(fixture.Js.Calls, call => call.Name == "setTrucks")
    );
    await component.InvokeAsync(
      () => component.Instance.OnTruckSelected(fixture.TruckA.ToString())
    );
    var summary = component.Find(".fleet-map-mobile-summary__remaining");
    await component.InvokeAsync(
      () =>
        component.Instance.OnMapInspectorChanged(
          "truck",
          fixture.TruckA.ToString(),
          10
        )
    );
    var calls = fixture.HttpCalls;
    await component.InvokeAsync(
      () =>
        component.Instance.OnRouteProgress(fixture.TruckA.ToString(), 1617, 10)
    );
    Assert.Equal("20", summary.QuerySelector("strong")!.TextContent);
    for (var i = 0; i < 2; i++)
    {
      await component.InvokeAsync(
        () =>
          component.Instance.OnMapBackgroundClicked(
            fixture.TruckA.ToString(),
            10
          )
      );
      AssertTruckPanelsVisible(component);
      var current = Assert.Single(
        component.FindAll(".fleet-map-mobile-summary__remaining")
      );
      Assert.Equal("20", current.QuerySelector("strong")!.TextContent);
    }
    await component.InvokeAsync(
      () =>
        component.Instance.OnRouteProgress(fixture.TruckA.ToString(), 0, 1627)
    );
    Assert.Equal("0", summary.QuerySelector("strong")!.TextContent);
    Assert.Equal(calls, fixture.HttpCalls);
  }

  [Fact]
  public async Task TruckPanelsStayVisibleInOrderWithoutRequestsOrCameraChanges()
  {
    using var fixture = new SelectionFixture();
    var component = fixture.Render();
    component.WaitForAssertion(
      () => Assert.Contains(fixture.Js.Calls, call => call.Name == "setTrucks")
    );
    await component.InvokeAsync(
      () => component.Instance.OnTruckSelected(fixture.TruckA.ToString())
    );
    await component.InvokeAsync(
      () =>
        component.Instance.OnMapInspectorChanged(
          "truck",
          fixture.TruckA.ToString(),
          10
        )
    );
    var requests = fixture.HttpCalls;
    var publications = fixture.Js.Calls.Count;
    AssertTruckPanelsVisible(component);
    var route = component.Find("#fleet-map-route-details");
    var routeText = route.TextContent;
    var readings = component.Find("#fleet-map-telemetry-details").TextContent;
    Assert.Contains(
      "54777",
      component.Find(".fleet-map-inspector__title").TextContent
    );
    for (var click = 0; click < 2; click++)
    {
      await component.InvokeAsync(
        () =>
          component.Instance.OnMapBackgroundClicked(
            fixture.TruckA.ToString(),
            10
          )
      );
      AssertTruckPanelsVisible(component);
      Assert.Equal(
        routeText,
        component.Find("#fleet-map-route-details").TextContent
      );
      Assert.Equal(
        readings,
        component.Find("#fleet-map-telemetry-details").TextContent
      );
    }
    Assert.Equal(requests, fixture.HttpCalls);
    Assert.Equal(publications, fixture.Js.Calls.Count);
    await component.InvokeAsync(
      () => component.Instance.OnTruckSelected(fixture.TruckB.ToString())
    );
    AssertTruckPanelsVisible(component);
    Assert.Equal(0, fixture.FuelWrites);
  }

  [Fact]
  public async Task ChangedStopsHideOldRouteAndForecastWithoutLosingTheFuelCalculationDispatch()
  {
    using var fixture = new SelectionFixture { DeferDetails = true };
    var now = fixture.Clock.GetUtcNow().UtcDateTime;
    var saved = fixture.Plan(fixture.TruckA);
    var stop = new PlanStop(
      Guid.NewGuid(),
      "Florida delivery",
      "Old Florida warehouse",
      2,
      new(27, -80)
    )
    {
      Job = "Drop Off",
      ScheduledTime = new(5, 0),
    };
    saved.State!.Plan!.Stops = [stop];
    saved.State.Plan.Tracking.NextStopId = stop.Id;
    saved.State.Plan.OriginalPlannedMiles = 1500;
    var eta = new DispatchEta(
      now,
      now.AddMinutes(2),
      [
        new(stop.Id, now.AddHours(1), "UTC", null, 0, 60, 0)
        {
          DispatchId = saved.DispatchId!.Value,
        },
      ],
      null,
      []
    );
    fixture.SetEta(fixture.TruckA, eta);
    var component = fixture.Render();
    component.WaitForAssertion(
      () => Assert.Contains(fixture.Js.Calls, x => x.Name == "setTrucks")
    );
    var selection = component.InvokeAsync(
      () => component.Instance.OnTruckSelected(fixture.TruckA.ToString())
    );
    (await fixture.ReadDetailsAsync()).Reply(
      new DispatchResponse
      {
        Id = saved.DispatchId.Value,
        LoadNumber = 1383,
        Stops =
        [
          new()
          {
            Id = Guid.NewGuid(),
            Sequence = 1,
            Name = "Webster pickup",
            Job = "Pick Up",
            Address = "Current Webster warehouse",
            ScheduledTime = new(2, 0),
          },
        ],
      }
    );
    await selection;
    await component.InvokeAsync(
      () =>
        component.Instance.OnRouteProgress(fixture.TruckA.ToString(), 900, 600)
    );
    Assert.Contains(
      "Old Florida warehouse",
      component.Find("[aria-label='Current dispatch route']").TextContent
    );

    var invalidated = JsonSerializer.Deserialize<AutomaticPlanningResult>(
      JsonSerializer.Serialize(fixture.Plan(fixture.TruckA))
    )!;
    invalidated.State!.Plan!.InputsChanged = true;
    invalidated.State.Plan.Stops[0] = stop with
    {
      Name = "Amsterdam pickup",
      Job = "Pick Up",
      ScheduledTime = new(11, 0),
    };
    fixture.SetPlanningResult(fixture.TruckA, invalidated);
    await component.InvokeAsync(
      () => fixture.Clock.Advance(TimeSpan.FromSeconds(10))
    );

    component.WaitForAssertion(() =>
    {
      using var payload = fixture.LastCurrentPayload();
      Assert.Equal(JsonValueKind.Null, payload.RootElement.ValueKind);
      Assert.Null(fixture.Js.Calls.Last(x => x.Name == "setStopEtas").Args![0]);
      Assert.Equal(JsonValueKind.Null, fixture.LastLoadReference().ValueKind);
      var panel = component.Find("[aria-label='Current dispatch route']");
      Assert.DoesNotContain("Old Florida warehouse", panel.TextContent);
      Assert.DoesNotContain("Amsterdam pickup", panel.TextContent);
      Assert.Contains("02:00\u00a0AM", panel.TextContent);
      Assert.All(
        panel.QuerySelectorAll(".fleet-map-route-info__metric strong"),
        value => Assert.Equal("— mi", value.TextContent)
      );
      Assert.Empty(panel.QuerySelectorAll(".arrival-estimate__ontime"));
      Assert.False(
        component
          .Find("button[aria-label='Fuel plan']")
          .HasAttribute("disabled")
      );
    });
    await component.InvokeAsync(
      () =>
        component.Instance.OnRouteProgress(fixture.TruckA.ToString(), 899, 601)
    );
    Assert.All(
      component.FindAll(".fleet-map-route-info__metric strong"),
      value => Assert.Equal("— mi", value.TextContent)
    );
    await component
      .Find("button[aria-label='Fuel plan']")
      .ClickAsync(new MouseEventArgs());
    Assert.Equal(
      saved.DispatchId,
      component.FindComponent<FuelPlanEditor>().Instance.DispatchId
    );
  }

  [Theory]
  [InlineData(null)]
  [InlineData(false)]
  [InlineData(true)]
  public async Task SinglePumpOpensEditorWithoutCalculatingForEmptyAutomaticAndManualPlans(
    bool? manual
  )
  {
    using var fixture = new SelectionFixture();
    if (manual.HasValue)
      fixture.Plan(fixture.TruckA).State!.Plan!.FuelPlan = new()
      {
        TruckId = fixture.TruckA,
        ManuallyEdited = manual.Value,
      };
    var component = fixture.Render();
    component.WaitForAssertion(
      () => Assert.Contains(fixture.Js.Calls, call => call.Name == "setTrucks")
    );
    await component.InvokeAsync(
      () => component.Instance.OnTruckSelected(fixture.TruckA.ToString())
    );
    var pump = Assert.Single(
      component.FindAll("button[aria-label='Fuel plan']")
    );
    Assert.Empty(
      component.FindAll(
        "button[aria-label='Edit fuel plan'], button[aria-label='Calculate Fuel']"
      )
    );
    await pump.ClickAsync(new MouseEventArgs());
    component.WaitForAssertion(
      () => Assert.Single(component.FindAll(".fuel-plan-editor"))
    );
    Assert.Equal(1, fixture.FuelPreviewCalls);
    Assert.Equal(0, fixture.FuelWrites);
    component.WaitForAssertion(
      () =>
        Assert.False(
          component
            .FindAll("button")
            .Single(button => button.TextContent == "Calculate automatically")
            .HasAttribute("disabled")
        )
    );
    Assert.True(
      component.Find("button[aria-label='Fuel plan']").HasAttribute("disabled")
    );
  }

  [Theory]
  [InlineData(false)]
  [InlineData(true)]
  public async Task FuelSaveOrResetSupersedesPendingPollAndAllCachedDispatchAliases(
    bool reset
  )
  {
    using var fixture = new SelectionFixture();
    var original = fixture.Plan(fixture.TruckA);
    original.State!.Plan!.FuelPlan = new()
    {
      TruckId = fixture.TruckA,
      PurchaseGallons = 20,
      CalculatedAt = DateTime.UtcNow,
    };
    fixture.StoreDispatchAlias(original);
    var stale = JsonSerializer.Deserialize<AutomaticPlanningResult>(
      JsonSerializer.Serialize(original)
    )!;
    var updated = JsonSerializer.Deserialize<AutomaticPlanningResult>(
      JsonSerializer.Serialize(original)
    )!;
    updated.State!.Plan!.FuelPlan!.PurchaseGallons = 80;
    updated.State.Plan.FuelPlan.ManuallyEdited = !reset;
    var component = fixture.Render();
    component.WaitForAssertion(
      () => Assert.Contains(fixture.Js.Calls, call => call.Name == "setTrucks")
    );
    await component.InvokeAsync(
      () => component.Instance.OnTruckSelected(fixture.TruckA.ToString())
    );
    await component
      .Find("button[aria-label='Fuel plan']")
      .ClickAsync(new MouseEventArgs());
    component.WaitForAssertion(
      () => Assert.Single(component.FindAll(".fuel-plan-editor"))
    );
    Assert.Equal(
      fixture.TruckA.ToString(),
      fixture.Js.Calls.Last(call => call.Name == "setFuelEditorTruck").Args![0]
    );
    Assert.False(
      component.Find(".fleet-map-page__background").HasAttribute("inert")
    );
    Assert.False(
      component.Find(".fleet-map-page__background").HasAttribute("aria-hidden")
    );
    Assert.Single(component.FindAll("#fleet-map"));
    Assert.Empty(
      component.FindAll(
        ".fleet-map-stage--fuel-editor, .fuel-plan-editor__map-slot"
      )
    );
    Assert.Null(component.Find("#fleet-map").Closest("[inert]"));
    var editor = component.FindComponent<FuelPlanEditor>();
    fixture.DeferPlanning = true;
    await component.InvokeAsync(
      () => fixture.Clock.Advance(TimeSpan.FromSeconds(10))
    );
    var olderRead = await fixture.ReadPlanningAsync();
    if (reset)
      await component.InvokeAsync(
        () => editor.Instance.Reset.InvokeAsync(updated)
      );
    else
    {
      var saved = component.InvokeAsync(
        () =>
          editor.Instance.Saved.InvokeAsync(
            new FuelPlanEditPreview(
              updated.State.Plan.FuelPlan,
              [],
              original.State.Plan.FuelPlan.CalculatedAt,
              200,
              200,
              []
            )
          )
      );
      var forcedRead = await fixture.ReadPlanningAsync();
      forcedRead.Reply(updated);
      await saved;
    }
    olderRead.Reply(stale);
    component.WaitForAssertion(() =>
    {
      Assert.Empty(component.FindAll(".fuel-plan-editor"));
      Assert.Null(
        fixture.Js.Calls.Last(call => call.Name == "setFuelEditorTruck").Args![
          0
        ]
      );
      using var payload = fixture.LastCurrentPayload();
      Assert.Equal(
        80,
        payload
          .RootElement.GetProperty("fuelPlan")
          .GetProperty("purchaseGallons")
          .GetDouble()
      );
      Assert.Equal(
        80,
        fixture
          .CachedPlan(fixture.TruckA)!
          .State!.Plan!.FuelPlan!.PurchaseGallons
      );
      Assert.Equal(
        80,
        fixture
          .CachedDispatchPlan(original.DispatchId!.Value)!
          .State!.Plan!.FuelPlan!.PurchaseGallons
      );
    });
  }

  [Fact]
  public async Task FuelEditorRejectsOldSelectionCallbacksAndClosesWhenCurrentTruckChanges()
  {
    using var fixture = new SelectionFixture();
    var component = fixture.Render();
    component.WaitForAssertion(
      () => Assert.Contains(fixture.Js.Calls, call => call.Name == "setTrucks")
    );
    await component.InvokeAsync(
      () => component.Instance.OnTruckSelected(fixture.TruckA.ToString())
    );
    var station = Guid.NewGuid().ToString();
    await component.InvokeAsync(
      () =>
        component.Instance.OnFuelStationEdit(
          fixture.TruckB.ToString(),
          fixture.Plan(fixture.TruckB).DispatchId!.Value.ToString(),
          station,
          "Wrong station",
          null,
          false
        )
    );
    Assert.Empty(component.FindAll(".fuel-plan-editor"));
    AssertTruckPanelsVisible(component);
    await component.InvokeAsync(
      () =>
        component.Instance.OnFuelStationEdit(
          fixture.TruckA.ToString(),
          fixture.Plan(fixture.TruckA).DispatchId!.Value.ToString(),
          station,
          "Selected station",
          null,
          false
        )
    );
    component.WaitForAssertion(
      () => Assert.Single(component.FindAll(".fuel-plan-editor"))
    );
    Assert.False(
      component
        .Find(".fleet-map-info-reserved")
        .ClassList.Contains("has-selection")
    );
    Assert.Contains(fixture.Js.Calls, call => call.Name == "closeStationPopup");
    Assert.True(
      component.Find("button[aria-label='Fuel plan']").HasAttribute("disabled")
    );
    var oldEditor = component.FindComponent<FuelPlanEditor>().Instance;
    await component.InvokeAsync(
      () => component.Instance.OnTruckSelected(fixture.TruckB.ToString())
    );
    Assert.Empty(component.FindAll(".fuel-plan-editor"));
    Assert.False(
      component.Find(".fleet-map-page__background").HasAttribute("inert")
    );
    await component.InvokeAsync(
      () =>
        component.Instance.OnFuelStationEdit(
          fixture.TruckA.ToString(),
          fixture.Plan(fixture.TruckA).DispatchId!.Value.ToString(),
          station,
          "Late station",
          null,
          false
        )
    );
    Assert.Empty(component.FindAll(".fuel-plan-editor"));
    await component.InvokeAsync(
      () =>
        component
          .Find("button[aria-label='Fuel plan']")
          .ClickAsync(new MouseEventArgs())
    );
    var currentEditor = component.FindComponent<FuelPlanEditor>().Instance;
    var returns = fixture.Js.Calls.Count(call =>
      call.Name == "clearFuelStationFocus"
    );
    await component.InvokeAsync(() => oldEditor.Closed.InvokeAsync());
    Assert.Same(
      currentEditor,
      component.FindComponent<FuelPlanEditor>().Instance
    );
    Assert.Equal(
      returns,
      fixture.Js.Calls.Count(call => call.Name == "clearFuelStationFocus")
    );
  }

  [Fact]
  public async Task ColdRouteReadReservesTheSamePanelColumnsWithoutLifecycleMessages()
  {
    using var fixture = new SelectionFixture(cacheCurrentPlans: false)
    {
      DeferPreview = true,
      DeferPlanning = true,
    };
    var component = fixture.Render();
    component.WaitForAssertion(
      () => Assert.Contains(fixture.Js.Calls, call => call.Name == "setTrucks")
    );
    var mapMarkup = component.Find("#fleet-map").OuterHtml;
    var selecting = component.InvokeAsync(
      () => component.Instance.OnTruckSelected(fixture.TruckA.ToString())
    );
    var preview = await fixture.ReadPreviewAsync();
    component.WaitForAssertion(() =>
    {
      var panel = component.Find("[aria-label='Current dispatch route']");
      Assert.Equal("true", panel.GetAttribute("aria-busy"));
      var routeOptions = component.Find("button[aria-label='Route options']");
      Assert.True(routeOptions.HasAttribute("disabled"));
      Assert.Contains("map-action-icon", routeOptions.ClassList);
      Assert.Single(routeOptions.QuerySelectorAll("svg"));
      Assert.Empty(routeOptions.TextContent.Trim());
      Assert.Equal("Route options", routeOptions.GetAttribute("title"));
      Assert.Contains(
        "Order —",
        panel
          .QuerySelector(".fleet-map-route-info__load-reference")!
          .TextContent
      );
      Assert.Equal(
        "—",
        panel
          .QuerySelector(
            ".fleet-map-route-info__load-reference .fleet-map-inspector__value"
          )!
          .TextContent
      );
      Assert.NotNull(
        panel.QuerySelector(
          ".fleet-map-route-info__total > strong.fleet-map-inspector__value"
        )
      );
      // ETA is reserved in the card head now; the route keeps the cycle.
      Assert.Contains(
        "Cycle remaining",
        panel
          .QuerySelector(".fleet-map-route-info__eta-placeholder")!
          .TextContent
      );
      Assert.Contains(
        "ETA",
        component.Find(".fleet-map-inspector__arrival-placeholder").TextContent
      );
      Assert.Equal(
        new[] { "Remaining" },
        panel
          .QuerySelectorAll(
            ".fleet-map-route-info__metric .fleet-map-route-info__label"
          )
          .Select(x => x.TextContent)
      );
      Assert.Single(
        panel.QuerySelectorAll(
          ":scope > .fleet-map-route-info__distances > .fleet-map-route-info__metric"
        )
      );
      Assert.NotNull(
        panel.QuerySelector(
          ".fleet-map-route-info__visit-heading > .fleet-map-route-info__distance"
        )
      );
      Assert.NotNull(
        panel.QuerySelector(":scope > .fleet-map-route-info__load")
      );
      Assert.NotNull(
        panel.QuerySelector(
          ":scope > .fleet-map-route-info__visit > .fleet-map-route-info__next"
        )
      );
      Assert.Single(
        panel.QuerySelectorAll(":scope > .fleet-map-route-info__visit > *")
      );
      Assert.NotNull(
        panel.QuerySelector(
          ":scope > .fleet-map-route-info__timing > .fleet-map-route-info__appointment"
        )
      );
      Assert.Contains("Total — mi · — km", panel.TextContent);
      Assert.NotNull(
        panel.QuerySelector(
          ":scope > .fleet-map-route-info__timing > .fleet-map-route-info__appointment + .arrival-estimate"
        )
      );
      Assert.DoesNotContain("Loading saved route", component.Markup);
      Assert.Equal(
        mapMarkup,
        component.Find(".fleet-map-stage > #fleet-map").OuterHtml
      );
      Assert.Single(fixture.Js.Calls, call => call.Name == "createFleetMap");
      Assert.NotNull(
        panel.Closest(".fleet-map-stage > .fleet-map-info-reserved")
      );
    });

    var originalPanel = component.Find("[aria-label='Current dispatch route']");
    var originalGroups = originalPanel
      .Children.Select(group => group.ClassName)
      .ToArray();
    Assert.Equal("fleet-map-route-details", originalPanel.ParentElement!.Id);
    var originalArrival = component.FindComponent<ArrivalEstimate>().Instance;
    preview.Reply(fixture.Plan(fixture.TruckA));
    var refresh = await fixture.ReadPlanningAsync();
    component.WaitForAssertion(() =>
    {
      var panel = component.Find("[aria-label='Current dispatch route']");
      Assert.Equal("true", panel.GetAttribute("aria-busy"));
      Assert.Equal("fleet-map-route-details", panel.ParentElement!.Id);
      Assert.Equal(
        originalGroups,
        panel.Children.Select(group => group.ClassName).ToArray()
      );
      Assert.Same(
        originalArrival,
        component.FindComponent<ArrivalEstimate>().Instance
      );
      Assert.False(
        component
          .Find("button[aria-label='Route options']")
          .HasAttribute("disabled")
      );
      Assert.Single(
        panel.QuerySelectorAll(
          ":scope > .fleet-map-route-info__distances > .fleet-map-route-info__metric"
        )
      );
      Assert.NotNull(
        panel.QuerySelector(
          ".fleet-map-route-info__visit-heading > .fleet-map-route-info__distance"
        )
      );
      Assert.NotNull(
        panel.QuerySelector(":scope > .fleet-map-route-info__load")
      );
      Assert.NotNull(
        panel.QuerySelector(
          ":scope > .fleet-map-route-info__visit > .fleet-map-route-info__next"
        )
      );
      Assert.Single(
        panel.QuerySelectorAll(":scope > .fleet-map-route-info__visit > *")
      );
      Assert.NotNull(
        panel.QuerySelector(
          ":scope > .fleet-map-route-info__timing > .fleet-map-route-info__appointment"
        )
      );
      Assert.NotNull(
        panel.QuerySelector(
          ":scope > .fleet-map-route-info__timing > .fleet-map-route-info__appointment + .arrival-estimate"
        )
      );
      Assert.Contains(
        fixture
          .Plan(fixture.TruckA)
          .State!.Plan!.OriginalPlannedMiles.ToString("N0"),
        panel.QuerySelector(".fleet-map-route-info__total")!.TextContent
      );
      Assert.DoesNotContain("Loading saved route", component.Markup);
      Assert.Equal(
        mapMarkup,
        component.Find(".fleet-map-stage > #fleet-map").OuterHtml
      );
      Assert.Single(fixture.Js.Calls, call => call.Name == "createFleetMap");
    });
    refresh.Reply(fixture.Plan(fixture.TruckA));
    await selecting;
    Assert.Equal("false", originalPanel.GetAttribute("aria-busy"));
    Assert.Same(
      originalArrival,
      component.FindComponent<ArrivalEstimate>().Instance
    );
  }

  [Theory]
  [InlineData(true)]
  [InlineData(false)]
  public async Task CurrentRouteAddressShowsNormalStreetThenBoldLocalityAndCopiesTheOriginal(
    bool hasJob
  )
  {
    using var fixture = new SelectionFixture();
    var plan = fixture.Plan(fixture.TruckA).State!.Plan!;
    var stop = new PlanStop(
      Guid.NewGuid(),
      "Mid 972",
      " 50 Patriot Dr, Middletown, DE 19709, US ",
      1,
      new(40, -80)
    )
    {
      Job = hasJob ? "Delivery" : "",
    };
    plan.Stops = [stop];
    plan.Tracking.NextStopId = stop.Id;
    plan.Tracking.NextStopLabel = "Delivery · Middletown, DE";
    var component = fixture.Render();
    component.WaitForAssertion(
      () => Assert.Contains(fixture.Js.Calls, call => call.Name == "setTrucks")
    );
    await component.InvokeAsync(
      () => component.Instance.OnTruckSelected(fixture.TruckA.ToString())
    );
    var next = component.Find(
      "[aria-label='Current dispatch route'] .fleet-map-route-info__next"
    );
    Assert.Equal(
      "Delivery",
      next.QuerySelector(".fleet-map-route-info__label")!.TextContent
    );
    var address = next.QuerySelector("[title='Copy full address']")!;
    var lines = address.QuerySelector(".fleet-map-route-info__address-lines")!;
    Assert.Equal(
      new[] { "Mid 972", "50 Patriot Dr", "Middletown, DE 19709, US" },
      lines.Children.Select(line => line.TextContent)
    );
    Assert.Contains(
      "fleet-map-route-info__facility",
      lines.Children[0].ClassList
    );
    Assert.Equal("SPAN", lines.Children[1].TagName);
    Assert.Equal("STRONG", lines.Children[2].TagName);
    Assert.Empty(lines.Children[1].QuerySelectorAll("strong, b"));
    Assert.Empty(address.QuerySelectorAll("svg"));
    Assert.Equal(stop.Address, lines.GetAttribute("title"));
    Assert.Contains(
      "fleet-map-route-info__street",
      lines.Children[1].ClassList
    );
    await component.InvokeAsync(
      () =>
        component
          .Find("[title='Copy full address']")
          .ClickAsync(new MouseEventArgs())
    );
    Assert.Contains(
      fixture.Js.Calls,
      call =>
        call.Name == "navigator.clipboard.writeText"
        && Equals(call.Args![0], stop.Address)
    );
  }

  [Theory]
  [InlineData("Drop Off", "Receiver Inc.", "Receiver Inc.")]
  [InlineData("Pick Up", " Shipper LLC ", "Shipper LLC")]
  [InlineData("Drop Off", "<em>Receiver</em>", "<em>Receiver</em>")]
  [InlineData("Drop Off", "  ", null)]
  public async Task CurrentStopFacilityUsesTheTrackedVisitWithoutInventingMissingNames(
    string job,
    string name,
    string? expected
  )
  {
    using var fixture = new SelectionFixture();
    var plan = fixture.Plan(fixture.TruckA).State!.Plan!;
    var stop = new PlanStop(
      Guid.NewGuid(),
      name,
      "2120 NC-71, Maxton, NC 28364, US",
      2,
      new(34.7, -79.3)
    )
    {
      Job = job,
    };
    plan.Stops =
    [
      stop with
      {
        Id = Guid.NewGuid(),
        Name = "Another facility at the same address",
        Sequence = 1,
      },
      stop,
    ];
    plan.Tracking.NextStopId = stop.Id;
    var component = fixture.Render();
    component.WaitForAssertion(
      () => Assert.Contains(fixture.Js.Calls, call => call.Name == "setTrucks")
    );
    await component.InvokeAsync(
      () => component.Instance.OnTruckSelected(fixture.TruckA.ToString())
    );
    var facilities = component.FindAll(".fleet-map-route-info__facility");
    if (expected is null)
      Assert.Empty(facilities);
    else
    {
      Assert.Equal(expected, Assert.Single(facilities).TextContent);
      Assert.Empty(facilities[0].Children);
    }
    Assert.DoesNotContain(
      "Another facility",
      component.Find(".fleet-map-route-info__next").TextContent
    );
    await component
      .Find("[title='Copy full address']")
      .ClickAsync(new MouseEventArgs());
    Assert.Equal(
      stop.Address,
      fixture
        .Js.Calls.Last(x => x.Name == "navigator.clipboard.writeText")
        .Args![0]
    );
    await component.InvokeAsync(
      () => component.Instance.OnTruckSelected(fixture.TruckB.ToString())
    );
    Assert.DoesNotContain(
      "Receiver",
      component.Find(".fleet-map-route-info__next").TextContent
    );
    Assert.DoesNotContain(
      "Shipper",
      component.Find(".fleet-map-route-info__next").TextContent
    );
    Assert.Equal(0, fixture.FuelWrites);
  }

  [Theory]
  [InlineData(true)]
  [InlineData(false)]
  public async Task UnifiedTruckHeaderKeepsDutyBelowClocksWithoutRecap(
    bool available
  )
  {
    using var fixture = new SelectionFixture();
    var now = fixture.Clock.GetUtcNow();
    var plan = fixture.Plan(fixture.TruckA).State!.Plan!;
    var stop = new PlanStop(
      Guid.NewGuid(),
      "Delivery",
      "50 Patriot Dr, Middletown, DE 19709, US",
      1,
      new(40, -80)
    );
    plan.Stops = [stop];
    plan.Tracking.NextStopId = stop.Id;
    var current = new StopCycleForecast(
      300,
      now.AddHours(12),
      185,
      "UTC",
      true
    );
    var future = new StopCycleForecast(700, now.AddDays(2), 660, "UTC", true);
    var eta = new DispatchEta(
      now.UtcDateTime,
      now.AddMinutes(2).UtcDateTime,
      [
        new(stop.Id, now.AddHours(1), "UTC", null, 0, 60, 0)
        {
          DispatchId = plan.DispatchId,
          CycleAfterDeparture = future,
        },
      ],
      null,
      []
    )
    {
      CycleAtCalculation = available ? current : null,
    };
    fixture.SetEta(fixture.TruckA, eta);
    var component = fixture.Render();
    component.WaitForAssertion(
      () => Assert.Contains(fixture.Js.Calls, call => call.Name == "setTrucks")
    );
    await component.InvokeAsync(
      () => component.Instance.OnTruckSelected(fixture.TruckA.ToString())
    );
    component.WaitForAssertion(() =>
    {
      // The unit names the card; "Truck" in front of it said nothing.
      Assert.Equal(
        "54777",
        component.Find(".fleet-map-inspector__title").TextContent
      );
      Assert.Equal(
        3,
        component
          .FindAll(
            ".fleet-map-truck-info__telemetry > .fleet-map-truck-info__reading"
          )
          .Count
      );
      Assert.Single(
        component.FindAll(".fleet-map-inspector__hours > .driver-hours-panel")
      );
      Assert.Empty(component.FindAll(".fleet-map-truck-info__hours-label"));
      Assert.Empty(component.FindAll(".driver-next-recap"));
      Assert.Single(
        component.FindAll(
          ".fleet-map-truck-info__reading--fuel > .fuel-reading"
        )
      );
      Assert.Single(
        component.FindAll(
          ".fleet-map-truck-info__reading--fuel > .fuel-reading--metric"
        )
      );
      Assert.Empty(
        component.FindAll(".fleet-map-truck-info .truck-illustration")
      );
      Assert.Single(
        component.FindAll(
          ".fleet-map-inspector__header [aria-label='Truck actions']"
        )
      );
      Assert.Single(component.FindAll(".fleet-map-inspector__driver"));
      Assert.Equal(
        2,
        component
          .FindAll(
            ".fleet-map-truck-info__reading > small > svg[aria-hidden='true']"
          )
          .Count
      );
      Assert.Empty(component.FindAll(".driver-duty"));
      Assert.DoesNotContain(
        "Next recap",
        component.Find(".fleet-map-truck-info").TextContent
      );
    });
    await component.InvokeAsync(
      () => component.Instance.OnTruckSelected(fixture.TruckB.ToString())
    );
    component.WaitForAssertion(() =>
    {
      Assert.Empty(component.FindAll(".driver-next-recap"));
      Assert.Single(
        component.FindAll(".fleet-map-inspector__hours > .driver-hours-panel")
      );
    });
  }

  [Fact]
  public async Task SelectionChangesOverlayTheSameMountedMapWithoutAddingFlowPanels()
  {
    using var fixture = new SelectionFixture();
    var component = fixture.Render();
    component.WaitForAssertion(
      () => Assert.Contains(fixture.Js.Calls, call => call.Name == "setTrucks")
    );
    var mapMarkup = component.Find(".fleet-map-stage > #fleet-map").OuterHtml;
    Assert.Single(
      component.FindAll(".fleet-map-stage > .fleet-map-info-reserved")
    );
    Assert.Empty(
      component.FindAll(".fleet-map-page__background .fleet-map-info-reserved")
    );
    Assert.False(
      component
        .Find(".fleet-map-info-reserved")
        .ClassList.Contains("has-selection")
    );
    Assert.Single(component.FindAll(".fleet-map-inspector__native[hidden]"));
    Assert.Empty(component.FindAll(".fleet-map-info-empty"));
    await component.InvokeAsync(
      () => component.Instance.OnTruckSelected(fixture.TruckA.ToString())
    );
    component.WaitForAssertion(
      () =>
        Assert.True(
          component
            .Find(".fleet-map-info-reserved")
            .ClassList.Contains("has-selection")
        )
    );
    Assert.Equal(
      mapMarkup,
      component.Find(".fleet-map-stage > #fleet-map").OuterHtml
    );
    Assert.Single(fixture.Js.Calls, call => call.Name == "createFleetMap");
    await component.InvokeAsync(
      () => component.Instance.OnTruckSelected(fixture.TruckB.ToString())
    );
    Assert.Equal(
      mapMarkup,
      component.Find(".fleet-map-stage > #fleet-map").OuterHtml
    );
    await component.InvokeAsync(
      () =>
        component
          .Find("[aria-label='Close map information']")
          .ClickAsync(new MouseEventArgs())
    );
    Assert.False(
      component
        .Find(".fleet-map-info-reserved")
        .ClassList.Contains("has-selection")
    );
    Assert.Empty(component.FindAll("#fleet-map-details"));
    Assert.Empty(component.FindAll(".fleet-map-truck-info"));
    Assert.Empty(component.FindAll(".fleet-map-route-info"));
    Assert.Equal("clearSelection", fixture.Js.Calls.Last().Name);
    await component.InvokeAsync(
      () =>
        component.Instance.OnMapInspectorChanged(
          "truck",
          fixture.TruckB.ToString(),
          100
        )
    );
    Assert.False(
      component
        .Find(".fleet-map-info-reserved")
        .ClassList.Contains("has-selection")
    );
    Assert.Equal(
      mapMarkup,
      component.Find(".fleet-map-stage > #fleet-map").OuterHtml
    );
    Assert.Single(fixture.Js.Calls, call => call.Name == "createFleetMap");
    await component.InvokeAsync(
      () => component.Instance.OnTruckSelected(fixture.TruckA.ToString())
    );
    Assert.True(
      component
        .Find(".fleet-map-info-reserved")
        .ClassList.Contains("has-selection")
    );
    Assert.Equal(
      mapMarkup,
      component.Find(".fleet-map-stage > #fleet-map").OuterHtml
    );
    Assert.Single(fixture.Js.Calls, call => call.Name == "createFleetMap");
  }

  [Theory]
  [InlineData("fuel")]
  [InlineData("stop")]
  [InlineData("next-stop")]
  public async Task BackgroundClickRestoresAndRetainsAllTruckPanels(string kind)
  {
    using var fixture = new SelectionFixture();
    var component = fixture.Render();
    component.WaitForAssertion(
      () => Assert.Contains(fixture.Js.Calls, c => c.Name == "setTrucks")
    );
    var truck = fixture.TruckA.ToString();
    await component.InvokeAsync(
      () => component.Instance.OnTruckSelected(truck)
    );
    await component.InvokeAsync(
      () => component.Instance.OnMapInspectorChanged(kind, truck, 10)
    );
    Assert.Empty(component.FindAll(".fleet-map-inspector__close"));
    Assert.Single(component.FindAll(".fleet-map-inspector__back"));
    var requests = fixture.HttpCalls;
    var clears = fixture.Js.Calls.Count(c => c.Name == "clearSelection");
    await component.InvokeAsync(
      () => component.Instance.OnMapBackgroundClicked(truck, 10)
    );
    Assert.Equal(
      "truck",
      component.Find(".fleet-map-inspector").GetAttribute("data-inspector-mode")
    );
    Assert.Equal(requests, fixture.HttpCalls);
    Assert.Equal(
      clears,
      fixture.Js.Calls.Count(c => c.Name == "clearSelection")
    );
    await component.InvokeAsync(
      () => component.Instance.OnMapInspectorChanged("truck", truck, 11)
    );
    await component.InvokeAsync(
      () => component.Instance.OnMapBackgroundClicked(truck, 11)
    );
    AssertTruckPanelsVisible(component);
    Assert.True(
      component.Find(".fleet-map-inspector").ClassList.Contains("has-selection")
    );
    Assert.Equal(
      clears,
      fixture.Js.Calls.Count(c => c.Name == "clearSelection")
    );
    await component.InvokeAsync(
      () => component.Instance.OnMapBackgroundClicked(truck, 10)
    );
    await component.InvokeAsync(
      () =>
        component.Instance.OnMapBackgroundClicked(fixture.TruckB.ToString(), 11)
    );
    Assert.True(
      component.Find(".fleet-map-inspector").ClassList.Contains("has-selection")
    );
    await component.InvokeAsync(
      () => component.Instance.OnMapBackgroundClicked(truck, 11)
    );
    Assert.True(
      component.Find(".fleet-map-inspector").ClassList.Contains("has-selection")
    );
    Assert.Equal(
      clears,
      fixture.Js.Calls.Count(c => c.Name == "clearSelection")
    );
    Assert.Equal(requests, fixture.HttpCalls);
    await component
      .Find(".fleet-map-inspector__close")
      .ClickAsync(new MouseEventArgs());
    Assert.False(
      component.Find(".fleet-map-inspector").ClassList.Contains("has-selection")
    );
    Assert.Equal(
      clears + 1,
      fixture.Js.Calls.Count(c => c.Name == "clearSelection")
    );
  }

  [Fact]
  public async Task BackgroundClickClosesStationWithoutSelectedTruck()
  {
    using var fixture = new SelectionFixture();
    var component = fixture.Render();
    component.WaitForAssertion(
      () => Assert.Contains(fixture.Js.Calls, c => c.Name == "setTrucks")
    );
    await component.InvokeAsync(
      () => component.Instance.OnMapInspectorChanged("fuel", null, 1)
    );
    Assert.Single(component.FindAll(".fleet-map-inspector__close"));
    Assert.Empty(component.FindAll(".fleet-map-inspector__back"));
    var requests = fixture.HttpCalls;
    await component.InvokeAsync(
      () => component.Instance.OnMapBackgroundClicked(null, 1)
    );
    Assert.False(
      component.Find(".fleet-map-inspector").ClassList.Contains("has-selection")
    );
    Assert.Equal(requests, fixture.HttpCalls);
  }

  [Fact]
  public async Task InspectorSwitchesOnePersistentHostAndRejectsStaleOrForeignCallbacks()
  {
    using var fixture = new SelectionFixture();
    var component = fixture.Render();
    component.WaitForAssertion(
      () => Assert.Contains(fixture.Js.Calls, call => call.Name == "setTrucks")
    );
    await component.InvokeAsync(
      () => component.Instance.OnTruckSelected(fixture.TruckA.ToString())
    );
    var header = component.Find("#fleet-map-details").TextContent;
    var httpCalls = fixture.HttpCalls;
    var mapMarkup = component.Find("#fleet-map").OuterHtml;
    await component.InvokeAsync(
      () =>
        component.Instance.OnMapInspectorChanged(
          "fuel",
          fixture.TruckA.ToString(),
          10
        )
    );
    Assert.Equal(
      "fuel",
      component.Find(".fleet-map-inspector").GetAttribute("data-inspector-mode")
    );
    Assert.False(
      component.Find(".fleet-map-inspector__native").HasAttribute("hidden")
    );
    Assert.True(component.Find("#fleet-map-details").HasAttribute("hidden"));
    Assert.Equal(header, component.Find("#fleet-map-details").TextContent);
    await component.InvokeAsync(
      () =>
        component.Instance.OnMapInspectorChanged(
          "stop",
          fixture.TruckA.ToString(),
          9
        )
    );
    await component.InvokeAsync(
      () =>
        component.Instance.OnMapInspectorChanged(
          "stop",
          fixture.TruckB.ToString(),
          11
        )
    );
    await component.InvokeAsync(
      () => component.Instance.OnMapInspectorChanged("stop", null, 12)
    );
    Assert.Equal(
      "fuel",
      component.Find(".fleet-map-inspector").GetAttribute("data-inspector-mode")
    );
    await component.InvokeAsync(
      () =>
        component.Instance.OnMapInspectorChanged(
          "stop",
          fixture.TruckA.ToString(),
          13
        )
    );
    Assert.Equal(
      "stop",
      component.Find(".fleet-map-inspector").GetAttribute("data-inspector-mode")
    );
    Assert.Single(component.FindAll(".fleet-map-inspector__native"));
    await component.InvokeAsync(
      () =>
        component
          .Find(".fleet-map-inspector__back")
          .ClickAsync(new MouseEventArgs())
    );
    Assert.Equal(
      "truck",
      component.Find(".fleet-map-inspector").GetAttribute("data-inspector-mode")
    );
    Assert.False(component.Find("#fleet-map-details").HasAttribute("hidden"));
    Assert.True(
      component.Find(".fleet-map-inspector__native").HasAttribute("hidden")
    );
    Assert.Equal(header, component.Find("#fleet-map-details").TextContent);
    Assert.Equal(httpCalls + 1, fixture.HttpCalls);
    Assert.Single(fixture.Js.Calls, call => call.Name == "setStations");
    Assert.Equal(mapMarkup, component.Find("#fleet-map").OuterHtml);
    Assert.Single(fixture.Js.Calls, call => call.Name == "createFleetMap");
    await component.InvokeAsync(
      () => component.Instance.OnTruckSelected(fixture.TruckB.ToString())
    );
    await component.InvokeAsync(
      () =>
        component.Instance.OnMapInspectorChanged(
          "fuel",
          fixture.TruckA.ToString(),
          14
        )
    );
    Assert.Equal(
      "truck",
      component.Find(".fleet-map-inspector").GetAttribute("data-inspector-mode")
    );
    await component.InvokeAsync(
      () =>
        component.Instance.OnMapInspectorChanged(
          "fuel",
          fixture.TruckB.ToString(),
          15
        )
    );
    Assert.Equal(
      "fuel",
      component.Find(".fleet-map-inspector").GetAttribute("data-inspector-mode")
    );
  }

  [Fact]
  public async Task StationOnlyInspectionNeedsNoTruckOrPlanningRequest()
  {
    using var fixture = new SelectionFixture();
    var component = fixture.Render();
    component.WaitForAssertion(
      () => Assert.Contains(fixture.Js.Calls, call => call.Name == "setTrucks")
    );
    var httpCalls = fixture.HttpCalls;
    await component.InvokeAsync(
      () => component.Instance.OnMapInspectorChanged("fuel", null, 1)
    );
    Assert.True(
      component.Find(".fleet-map-inspector").ClassList.Contains("has-selection")
    );
    Assert.False(
      component.Find(".fleet-map-inspector__native").HasAttribute("hidden")
    );
    Assert.Empty(
      component.FindAll(".fleet-map-inspector__back, #fleet-map-details")
    );
    await component.InvokeAsync(
      () =>
        component
          .Find(".fleet-map-inspector")
          .KeyDownAsync(new KeyboardEventArgs { Key = "Escape" })
    );
    Assert.False(
      component.Find(".fleet-map-inspector").ClassList.Contains("has-selection")
    );
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
    component.WaitForAssertion(
      () => Assert.Single(component.FindAll(".fleet-map-route-info"))
    );
    await fixture.Js.ReadCurrentPayloadAsync();
    await component.InvokeAsync(
      () =>
        component.Instance.OnMapInspectorChanged(
          "stop",
          fixture.TruckA.ToString(),
          1
        )
    );
    Assert.Equal(
      "stop",
      component.Find(".fleet-map-inspector").GetAttribute("data-inspector-mode")
    );
    await component.InvokeAsync(
      () =>
        component.Instance.OnMapInspectorChanged(
          "fuel",
          fixture.TruckB.ToString(),
          2
        )
    );
    Assert.Equal(
      "stop",
      component.Find(".fleet-map-inspector").GetAttribute("data-inspector-mode")
    );
    await component.InvokeAsync(
      () =>
        component
          .Find(".fleet-map-inspector__back")
          .ClickAsync(new MouseEventArgs())
    );
    var transition = fixture.Js.Calls.Last(call =>
      call.Name == "setInspectorMode"
    );
    Assert.Equal(fixture.TruckA.ToString(), transition.Args![1]);
    Assert.Equal(
      "truck",
      component.Find(".fleet-map-inspector").GetAttribute("data-inspector-mode")
    );
    using var payload = fixture.LastCurrentPayload();
    Assert.Equal(
      plan.DispatchId,
      payload.RootElement.GetProperty("dispatchId").GetGuid()
    );
  }

  [Fact]
  public void StartupQueryTargetReachesTheCameraBeforeTheFirstFleetSnapshot()
  {
    using var fixture = new Fixture();
    var component = fixture.Render(fixture.TruckId);
    component.WaitForAssertion(
      () => Assert.Contains(fixture.Js.Calls, call => call.Name == "focusTruck")
    );
    var calls = fixture.Js.Calls.ToArray();
    var options = Array.FindIndex(calls, call => call.Name == "setOptions");
    var snapshot = Array.FindIndex(calls, call => call.Name == "setTrucks");
    Assert.True(options >= 0 && snapshot > options);
    Assert.Equal(
      fixture.TruckId,
      JsonSerializer
        .SerializeToElement(calls[options].Args![0])
        .GetProperty("initialTruckId")
        .GetGuid()
    );
    var focus = calls.First(call => call.Name == "focusTruck");
    Assert.Equal(true, focus.Args![2]);
  }

  [Fact]
  public void FailedInitialLocationsRevealTheMapInsteadOfLeavingTheStartupHostHidden()
  {
    using var fixture = new Fixture { FailLocations = true };
    var component = fixture.Render();
    component.WaitForAssertion(
      () =>
        Assert.Contains(
          fixture.Js.Calls,
          call => call.Name == "finishInitialView"
        )
    );
    Assert.DoesNotContain(fixture.Js.Calls, call => call.Name == "setTrucks");
  }

  [Theory]
  [InlineData(false)]
  [InlineData(true)]
  public async Task FailedFuelCalculationRetainsTheValidPlanUntilNormalReadRevalidation(
    bool invalidated
  )
  {
    using var fixture = new SelectionFixture();
    var original = fixture.Plan(fixture.TruckA);
    original.State!.Plan!.FuelPlan = new()
    {
      TruckId = fixture.TruckA,
      ScheduleImpact = new(
        DateTime.UtcNow,
        true,
        true,
        false,
        false,
        5,
        0,
        [],
        null
      ),
      Stops =
      [
        new()
        {
          StationId = Guid.NewGuid(),
          VisitKey = "saved-visit",
          Name = "Saved fuel station",
          BuyGallons = 25,
          ArrivalGallons = 18,
          MilesAhead = 100,
        },
      ],
    };
    var component = fixture.Render();
    component.WaitForAssertion(
      () => Assert.Contains(fixture.Js.Calls, x => x.Name == "setTrucks")
    );
    await component.InvokeAsync(
      () => component.Instance.OnTruckSelected(fixture.TruckA.ToString())
    );
    Assert.Empty(component.FindAll(".fuel-plan-summary"));
    using (var payload = fixture.LastCurrentPayload())
      Assert.Equal(
        25,
        payload
          .RootElement.GetProperty("fuelPlan")
          .GetProperty("stops")[0]
          .GetProperty("buyGallons")
          .GetDouble()
      );
    await component
      .Find("button[aria-label='Fuel plan']")
      .ClickAsync(new MouseEventArgs());
    component.WaitForAssertion(
      () =>
        Assert.False(
          component
            .FindAll("button")
            .Single(button => button.TextContent == "Calculate automatically")
            .HasAttribute("disabled")
        )
    );
    Assert.Equal(0, fixture.FuelWrites);
    fixture.DeferPlanning = true;
    var calculating = component
      .FindAll("button")
      .Single(button => button.TextContent == "Calculate automatically")
      .ClickAsync(new MouseEventArgs());
    var revalidation = await fixture.ReadPlanningAsync();
    Assert.Empty(component.FindAll(".fuel-plan-summary"));
    using (var payload = fixture.LastCurrentPayload())
      Assert.Equal(
        25,
        payload
          .RootElement.GetProperty("fuelPlan")
          .GetProperty("stops")[0]
          .GetProperty("buyGallons")
          .GetDouble()
      );
    Assert.Single(component.FindAll("[aria-label='Current dispatch route']"));
    Assert.NotNull(fixture.CachedPlan(fixture.TruckB));
    var refreshed = JsonSerializer.Deserialize<AutomaticPlanningResult>(
      JsonSerializer.Serialize(original)
    )!;
    if (invalidated)
    {
      refreshed.State!.Plan!.FuelPlan!.NeedsRefresh = true;
      refreshed.State.Plan.FuelPlan.RefreshReasons =
      [
        "Assignments changed. Recalculate fuel.",
      ];
    }
    revalidation.Reply(refreshed);
    await calculating;
    Assert.Equal(1, fixture.FuelWrites);
    Assert.Single(component.FindAll(".fuel-plan-editor"));
    Assert.Contains(
      "Fuel calculation unavailable.",
      component.Find("[role='alert']").TextContent
    );

    Assert.Empty(component.FindAll(".fuel-plan-summary"));
    Assert.DoesNotContain(
      "Assignments changed. Recalculate fuel.",
      component.Markup
    );
    Assert.DoesNotContain("Buy 25 gal", component.Markup);
    using (var payload = fixture.LastCurrentPayload())
    {
      var fuel = payload.RootElement.GetProperty("fuelPlan");
      Assert.Equal(invalidated, fuel.GetProperty("needsRefresh").GetBoolean());
      Assert.Equal(
        25,
        fuel.GetProperty("stops")[0].GetProperty("buyGallons").GetDouble()
      );
    }
    Assert.Single(component.FindAll("[aria-label='Current dispatch route']"));
    Assert.NotNull(fixture.CachedPlan(fixture.TruckB));
  }

  [Fact]
  public async Task StationToggleReusesLoadedDateAndTheFleetPriceBasisDoesNotRequestStationsAgain()
  {
    using var fixture = new Fixture();
    var component = fixture.Render();
    component.WaitForAssertion(
      () => Assert.Contains(fixture.Js.Calls, x => x.Name == "setTrucks")
    );
    Assert.Empty(component.FindAll(".fleet-map-key__fuel"));
    await component.InvokeAsync(
      () =>
        Toggle(component, "Fuel Stations")
          .ChangeAsync(new ChangeEventArgs { Value = true })
    );
    Assert.Equal(1, fixture.StationCalls);
    Assert.Single(fixture.Js.Calls, x => x.Name == "setStations");
    Assert.Equal(
      "Fuel price",
      component.Find(".fleet-map-key__fuel-label").TextContent
    );
    Assert.Equal(
      "Within each currency · zoom in for stations",
      component.Find(".fleet-map-key__note").TextContent
    );
    Assert.Equal(
      "No price",
      component.Find(".fleet-map-key__missing").TextContent
    );
    await component.InvokeAsync(
      () =>
        Toggle(component, "Fuel Stations")
          .ChangeAsync(new ChangeEventArgs { Value = false })
    );
    Assert.Empty(component.FindAll(".fleet-map-key__fuel"));
    await component.InvokeAsync(
      () =>
        Toggle(component, "Fuel Stations")
          .ChangeAsync(new ChangeEventArgs { Value = true })
    );
    // The price basis is the fleet's planning setting, not a map switch, and
    // learning it repaints the markers without asking for the stations again.
    Assert.Equal(1, fixture.StationCalls);
    Assert.Single(fixture.Js.Calls, x => x.Name == "setStations");
    Assert.DoesNotContain(component.Markup, "IFTA");
  }

  // The IFTA switch left the map. What the map paints is now whatever the
  // fleet plans on, and it says so without waiting for the answer to start.
  [Fact]
  public async Task FleetPriceBasisReachesTheMarkersWithoutHoldingUpTheMap()
  {
    using var fixture = new Fixture { FleetUsesIfta = false };
    var component = fixture.Render();
    component.WaitForAssertion(
      () => Assert.Contains(fixture.Js.Calls, x => x.Name == "setTrucks")
    );
    var created = Assert.Single(fixture.Js.Calls, x => x.Name == "setOptions");
    Assert.True(
      JsonSerializer
        .SerializeToElement(created.Args![0])
        .GetProperty("useIfta")
        .GetBoolean(),
      "the map starts on the planner's default rather than waiting"
    );
    component.WaitForAssertion(
      () =>
        Assert.Equal(
          false,
          fixture.Js.Calls.Last(x => x.Name == "setIfta").Args![0]
        )
    );
    Assert.DoesNotContain(component.Markup, "IFTA");
  }

  [Fact]
  public async Task OpeningFuelInspectionLoadsQuotesWithoutEnablingStationMarkers()
  {
    using var fixture = new Fixture();
    var component = fixture.Render();
    component.WaitForAssertion(
      () => Assert.Contains(fixture.Js.Calls, x => x.Name == "setTrucks")
    );
    await component.InvokeAsync(
      () => component.Instance.OnMapInspectorChanged("fuel", null, 1)
    );
    Assert.Equal(1, fixture.StationCalls);
    Assert.False(Toggle(component, "Fuel Stations").HasAttribute("checked"));
    Assert.Empty(component.FindAll(".fleet-map-key__fuel"));
    Assert.Single(fixture.Js.Calls, x => x.Name == "setStations");
    await component.InvokeAsync(
      () => component.Instance.OnMapInspectorChanged("closed", null, 2)
    );
    await component.InvokeAsync(
      () => component.Instance.OnMapInspectorChanged("fuel", null, 3)
    );
    Assert.Equal(1, fixture.StationCalls);
  }

  [Fact]
  public async Task TruckSearchFocusesWithoutAManualVisibilityToggle()
  {
    using var fixture = new Fixture();
    var component = fixture.Render();
    component.WaitForAssertion(
      () => Assert.Contains(fixture.Js.Calls, x => x.Name == "setTrucks")
    );
    Assert.DoesNotContain(
      component.FindAll(".fleet-map-toggle"),
      toggle => toggle.TextContent.Trim() == "Trucks"
    );
    await component
      .Find("#fleet-truck-search")
      .InputAsync(new ChangeEventArgs { Value = "54777" });
    Assert.DoesNotContain(
      fixture.Js.Calls,
      call => call.Name == "setTrucksVisible"
    );
    Assert.Contains(
      fixture.Js.Calls,
      x =>
        x.Name == "focusTruck" && Equals(x.Args![0], fixture.TruckId.ToString())
    );
    Assert.Single(component.FindAll("button[aria-label='Follow']"));
  }

  [Fact]
  public async Task SuccessfulUnchangedPollClearsAnErrorWithoutResendingNextLoadGeometry()
  {
    using var fixture = new Fixture();
    var component = fixture.Render();
    component.WaitForAssertion(
      () => Assert.Contains(fixture.Js.Calls, x => x.Name == "setTrucks")
    );
    await component.InvokeAsync(
      () => component.Instance.OnTruckSelected(fixture.TruckId.ToString())
    );
    await Toggle(component, "Next loads")
      .ChangeAsync(new ChangeEventArgs { Value = true });
    Assert.Single(fixture.Js.Calls, x => x.Name == "setNextLoadsBytes");
    // Two position polls pass without asking about upcoming loads again.
    for (var beat = 0; beat < 2; beat++)
    {
      var polls = fixture.Js.Calls.Count(x => x.Name == "setTrucks");
      await component.InvokeAsync(
        () => fixture.Clock.Advance(TimeSpan.FromSeconds(10))
      );
      component.WaitForAssertion(
        () =>
          Assert.True(
            fixture.Js.Calls.Count(x => x.Name == "setTrucks") > polls
          )
      );
    }
    Assert.Equal(1, fixture.NextCalls);
    await component.InvokeAsync(
      () => fixture.Clock.Advance(TimeSpan.FromSeconds(10))
    );
    component.WaitForAssertion(
      () =>
        Assert.Contains(
          "Next load routes could not be loaded.",
          component.Markup
        )
    );
    // A failed check is retried on the very next poll, not after the pause.
    await component.InvokeAsync(
      () => fixture.Clock.Advance(TimeSpan.FromSeconds(10))
    );
    component.WaitForAssertion(
      () =>
        Assert.DoesNotContain(
          "Next load routes could not be loaded.",
          component.Markup
        )
    );
    Assert.Equal(3, fixture.NextCalls);
    Assert.Single(fixture.Js.Calls, x => x.Name == "setNextLoadsBytes");
  }

  [Fact]
  public async Task OnOffOnRejectsTheEarlierResponseAndReusesTheLatestVisibleRoutes()
  {
    using var fixture = new SelectionFixture();
    var component = fixture.Render();
    component.WaitForAssertion(
      () => Assert.Contains(fixture.Js.Calls, x => x.Name == "setTrucks")
    );
    await component.InvokeAsync(
      () => component.Instance.OnTruckSelected(fixture.TruckA.ToString())
    );

    var firstToggle = Toggle(component, "Next loads")
      .ChangeAsync(new ChangeEventArgs { Value = true });
    var first = await fixture.ReadNextAsync();
    await Toggle(component, "Next loads")
      .ChangeAsync(new ChangeEventArgs { Value = false });
    Assert.True(first.Cancellation.IsCancellationRequested);
    var secondToggle = Toggle(component, "Next loads")
      .ChangeAsync(new ChangeEventArgs { Value = true });
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
    Assert.DoesNotContain(
      "Next load routes could not be loaded.",
      component.Markup
    );
    Assert.Equal(
      new[] { true, false, true },
      fixture
        .Js.Calls.Where(x => x.Name == "setNextLoadsVisible")
        .TakeLast(3)
        .Select(x => (bool)x.Args![0]!)
    );

    var clears = fixture.Js.Calls.Count(x => x.Name == "clearNextLoads");
    await Toggle(component, "Next loads")
      .ChangeAsync(new ChangeEventArgs { Value = false });
    var reopen = Toggle(component, "Next loads")
      .ChangeAsync(new ChangeEventArgs { Value = true });
    var unchanged = await fixture.ReadNextAsync();
    fixture.AssertIdentity(unchanged, fixture.TruckA, "latest");
    unchanged.ReplyUnchanged("latest");
    await reopen;
    fixture.AssertOnlyFuturePublished(latest.Id);
    Assert.Equal(
      clears,
      fixture.Js.Calls.Count(x => x.Name == "clearNextLoads")
    );
  }

  [Fact]
  public async Task ReturningToTruckAIgnoresBothEarlierAAndBResponsesAndKeepsTheNewRevision()
  {
    using var fixture = new SelectionFixture();
    var component = fixture.Render();
    component.WaitForAssertion(
      () => Assert.Contains(fixture.Js.Calls, x => x.Name == "setTrucks")
    );
    await component.InvokeAsync(
      () => component.Instance.OnTruckSelected(fixture.TruckA.ToString())
    );
    var initialToggle = Toggle(component, "Next loads")
      .ChangeAsync(new ChangeEventArgs { Value = true });
    var oldA = await fixture.ReadNextAsync();
    fixture.AssertIdentity(oldA, fixture.TruckA, "");

    var selectB = component.InvokeAsync(
      () => component.Instance.OnTruckSelected(fixture.TruckB.ToString())
    );
    var oldB = await fixture.ReadNextAsync();
    fixture.AssertIdentity(oldB, fixture.TruckB, "");
    Assert.True(oldA.Cancellation.IsCancellationRequested);

    var selectA = component.InvokeAsync(
      () => component.Instance.OnTruckSelected(fixture.TruckA.ToString())
    );
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
    Assert.Contains(
      "54777",
      component.Find(".fleet-map-inspector__header").TextContent
    );
    Assert.DoesNotContain(
      "Next load routes could not be loaded.",
      component.Markup
    );
    using var route = JsonDocument.Parse(
      (byte[])fixture.Js.Calls.Last(x => x.Name == "setRouteBytes").Args![0]!
    );
    Assert.Equal(
      fixture.TruckA,
      route.RootElement.GetProperty("truckId").GetGuid()
    );

    await component.InvokeAsync(
      () => fixture.Clock.Advance(TimeSpan.FromSeconds(10))
    );
    var poll = await fixture.ReadNextAsync();
    fixture.AssertIdentity(poll, fixture.TruckA, "current-a");
    var renders = component.RenderCount;
    poll.ReplyUnchanged("current-a");
    component.WaitForAssertion(
      () => Assert.True(component.RenderCount > renders)
    );
    Assert.DoesNotContain(
      "Next load routes could not be loaded.",
      component.Markup
    );
    fixture.AssertOnlyFuturePublished(latest.Id);
  }

  [Fact]
  public async Task EnabledNextLoadsStartAndDisplayWhileCurrentPlanningAndDetailsAreStillPending()
  {
    using var fixture = new SelectionFixture
    {
      DeferPlanning = true,
      DeferDetails = true,
    };
    var component = fixture.Render();
    component.WaitForAssertion(
      () => Assert.Contains(fixture.Js.Calls, x => x.Name == "setTrucks")
    );
    await Toggle(component, "Next loads")
      .ChangeAsync(new ChangeEventArgs { Value = true });

    var selection = component.InvokeAsync(
      () => component.Instance.OnTruckSelected(fixture.TruckA.ToString())
    );
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
    details.Reply(
      new { id = fixture.Plan(fixture.TruckA).DispatchId, loadNumber = 1358 }
    );
    await selection;
    Assert.Equal(1, fixture.NextCalls);
    Assert.Equal(0, fixture.PreviewCalls);
  }

  [Theory]
  [InlineData(false)]
  [InlineData(true)]
  public async Task CurrentPopupLoadReferenceArrivesIndependentlyOfRouteGeometry(
    bool detailsFirst
  )
  {
    using var fixture = new SelectionFixture(cacheCurrentPlans: false)
    {
      DeferPreview = true,
      DeferPlanning = true,
      DeferDetails = true,
    };
    var component = fixture.Render();
    component.WaitForAssertion(
      () => Assert.Contains(fixture.Js.Calls, x => x.Name == "setTrucks")
    );
    var selection = component.InvokeAsync(
      () => component.Instance.OnTruckSelected(fixture.TruckA.ToString())
    );
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
      details.Reply(
        new
        {
          id = saved.DispatchId,
          loadNumber = 1373,
          orderNumber = "566126837",
        }
      );
      component.WaitForAssertion(
        () =>
          Assert.Equal(
            "566126837",
            component.Find("[title='Copy order number']").TextContent
          )
      );
      Assert.Equal(JsonValueKind.Null, fixture.LastLoadReference().ValueKind);
      Assert.Equal(
        routeCalls,
        fixture.Js.Calls.Count(x => x.Name == "setRouteBytes")
      );
      Assert.False(planning.Response.Task.IsCompleted);
      planning.Reply(saved);
    }
    else
    {
      planning.Reply(saved);
      component.WaitForAssertion(() =>
      {
        using var current = fixture.LastCurrentPayload();
        Assert.Equal(
          saved.DispatchId,
          current.RootElement.GetProperty("dispatchId").GetGuid()
        );
        Assert.Equal(
          routeCalls + 1,
          fixture.Js.Calls.Count(x => x.Name == "setRouteBytes")
        );
        Assert.Equal(
          etaCalls + 1,
          fixture.Js.Calls.Count(x => x.Name == "setStopEtas")
        );
      });
      Assert.Equal(JsonValueKind.Null, fixture.LastLoadReference().ValueKind);
      Assert.False(details.Response.Task.IsCompleted);
      details.Reply(
        new
        {
          id = saved.DispatchId,
          loadNumber = 1373,
          orderNumber = "566126837",
        }
      );
    }

    await selection;
    fixture.AssertLoadReference(saved.DispatchId!.Value, 1373, "566126837");
    Assert.Single(
      fixture.Js.Calls,
      x => x.Name == "setLoadReference" && x.Args![0] is not null
    );
    Assert.Equal(
      routeCalls + 1,
      fixture.Js.Calls.Count(x => x.Name == "setRouteBytes")
    );
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
    component.WaitForAssertion(
      () => Assert.Contains(fixture.Js.Calls, x => x.Name == "setTrucks")
    );
    var selectA = component.InvokeAsync(
      () => component.Instance.OnTruckSelected(fixture.TruckA.ToString())
    );
    var detailsA = await fixture.ReadDetailsAsync();
    Assert.Equal(
      $"/api/dispatch/{fixture.Plan(fixture.TruckA).DispatchId}",
      detailsA.Uri.AbsolutePath
    );
    var selectB = component.InvokeAsync(
      () => component.Instance.OnTruckSelected(fixture.TruckB.ToString())
    );
    var detailsB = await fixture.ReadDetailsAsync();
    var current = fixture.Plan(fixture.TruckB).DispatchId!.Value;
    Assert.Equal($"/api/dispatch/{current}", detailsB.Uri.AbsolutePath);
    detailsB.Reply(
      new
      {
        id = current,
        loadNumber = 1373,
        orderNumber = "566126837",
      }
    );
    await selectB;
    fixture.AssertLoadReference(current, 1373, "566126837");
    var references = fixture.Js.Calls.Count(x => x.Name == "setLoadReference");
    var routes = fixture.Js.Calls.Count(x => x.Name == "setRouteBytes");
    var httpCalls = fixture.HttpCalls;

    detailsA.Reply(
      new
      {
        id = fixture.Plan(fixture.TruckA).DispatchId,
        loadNumber = 1358,
        orderNumber = "565606595",
      }
    );
    await selectA;
    fixture.AssertLoadReference(current, 1373, "566126837");
    Assert.Equal(
      references,
      fixture.Js.Calls.Count(x => x.Name == "setLoadReference")
    );
    Assert.Equal(
      routes,
      fixture.Js.Calls.Count(x => x.Name == "setRouteBytes")
    );
    Assert.Equal(httpCalls, fixture.HttpCalls);
    Assert.All(
      fixture.Js.Calls.Where(x =>
        x.Name == "setLoadReference" && x.Args![0] is not null
      ),
      call =>
        Assert.Equal(
          current,
          JsonSerializer
            .SerializeToElement(
              call.Args![0],
              new JsonSerializerOptions(JsonSerializerDefaults.Web)
            )
            .GetProperty("dispatchId")
            .GetGuid()
        )
    );
    Assert.Contains(
      "64888",
      component.Find(".fleet-map-inspector__header").TextContent
    );
    Assert.Equal(
      "1373",
      component.Find("[title='Copy load number']").TextContent
    );
    Assert.Equal(
      "566126837",
      component.Find("[title='Copy order number']").TextContent
    );
    Assert.DoesNotContain("565606595", component.Markup);
    using var route = fixture.LastCurrentPayload();
    Assert.Equal(
      fixture.TruckB,
      route.RootElement.GetProperty("truckId").GetGuid()
    );
  }

  [Fact]
  public async Task ColdSelectionWithUnavailablePreviewUsesTheResolvedCurrentIdentityWithoutASecondNextLoadRequest()
  {
    using var fixture = new SelectionFixture(cacheCurrentPlans: false)
    {
      DeferPlanning = true,
      PreviewUnavailable = true,
    };
    var component = fixture.Render();
    component.WaitForAssertion(
      () => Assert.Contains(fixture.Js.Calls, x => x.Name == "setTrucks")
    );
    await Toggle(component, "Next loads")
      .ChangeAsync(new ChangeEventArgs { Value = true });
    var selection = component.InvokeAsync(
      () => component.Instance.OnTruckSelected(fixture.TruckA.ToString())
    );
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
  public async Task ColdPreviewStartsNextLoadsBeforeLivePlanningOrDetailsAndReusesSavedGeometry(
    bool unsaved
  )
  {
    using var fixture = new SelectionFixture(cacheCurrentPlans: false)
    {
      DeferPreview = true,
      DeferPlanning = true,
      DeferDetails = true,
    };
    var component = fixture.Render();
    component.WaitForAssertion(
      () => Assert.Contains(fixture.Js.Calls, x => x.Name == "setTrucks")
    );
    await Toggle(component, "Next loads")
      .ChangeAsync(new ChangeEventArgs { Value = true });
    var selection = component.InvokeAsync(
      () => component.Instance.OnTruckSelected(fixture.TruckA.ToString())
    );
    var preview = await fixture.ReadPreviewAsync();
    Assert.Equal(0, fixture.PlanningCalls);
    Assert.Equal(0, fixture.NextCalls);
    var saved = fixture.Plan(fixture.TruckA);
    saved = saved with
    {
      State = saved.State! with
      {
        Progress = new(
          75,
          25,
          null,
          0,
          false,
          false,
          DateTime.UtcNow,
          new(40.5, -80)
        ),
      },
    };
    preview.Reply(unsaved ? saved with { State = null } : saved);
    var next = await fixture.ReadNextAsync();
    var planning = await fixture.ReadPlanningAsync();
    var details = await fixture.ReadDetailsAsync();
    fixture.AssertIdentity(next, fixture.TruckA, "");
    Assert.Equal(
      unsaved ? "" : $"?knownPlanId={saved.State!.Plan!.Id}&knownVersion=1",
      planning.Uri.Query
    );
    if (!unsaved)
    {
      using var current = fixture.LastCurrentPayload();
      Assert.Equal(
        saved.State!.Plan!.Id,
        current.RootElement.GetProperty("id").GetGuid()
      );
      Assert.Equal(
        2,
        current
          .RootElement.GetProperty("route")
          .GetProperty("legs")[0]
          .GetProperty("points")
          .GetArrayLength()
      );
      Assert.Equal(
        75,
        Assert
          .IsType<RouteProgress>(
            fixture.Js.Calls.Last(x => x.Name == "setRouteBytes").Args![1]
          )
          .ProgressMiles
      );
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
    Assert.Equal(
      2,
      fixture
        .CachedPlan(fixture.TruckA)!
        .State!.Plan!.Route.Legs[0]
        .Points.Count
    );
  }

  [Fact]
  public async Task EmptyPreviewDoesNotInferCurrentDispatchOrLoadUnrelatedDetails()
  {
    using var fixture = new SelectionFixture(cacheCurrentPlans: false)
    {
      DeferPreview = true,
      DeferPlanning = true,
    };
    var component = fixture.Render();
    component.WaitForAssertion(
      () => Assert.Contains(fixture.Js.Calls, x => x.Name == "setTrucks")
    );
    await Toggle(component, "Next loads")
      .ChangeAsync(new ChangeEventArgs { Value = true });
    var selection = component.InvokeAsync(
      () => component.Instance.OnTruckSelected(fixture.TruckA.ToString())
    );
    var empty = new AutomaticPlanningResult(
      fixture.TruckA,
      null,
      null,
      null,
      null
    );
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
    using var fixture = new SelectionFixture(cacheCurrentPlans: false)
    {
      DeferPreview = true,
      DeferPlanning = true,
    };
    var component = fixture.Render();
    component.WaitForAssertion(
      () => Assert.Contains(fixture.Js.Calls, x => x.Name == "setTrucks")
    );
    await Toggle(component, "Next loads")
      .ChangeAsync(new ChangeEventArgs { Value = true });
    var selection = component.InvokeAsync(
      () => component.Instance.OnTruckSelected(fixture.TruckA.ToString())
    );
    var preview = await fixture.ReadPreviewAsync();
    await component.InvokeAsync(
      () => fixture.Clock.Advance(TimeSpan.FromSeconds(2))
    );
    var planning = await fixture.ReadPlanningAsync();
    Assert.True(preview.Cancellation.IsCancellationRequested);
    Assert.False(preview.Response.Task.IsCompleted);
    Assert.Equal("", planning.Uri.Query);
    var latest = fixture.Plan(fixture.TruckA) with
    {
      Message = "Latest live result",
    };
    planning.Reply(latest);
    var next = await fixture.ReadNextAsync();
    fixture.AssertIdentity(next, fixture.TruckA, "");
    next.Reply("future", []);
    await selection;
    preview.Reply(fixture.Plan(fixture.TruckA));
    Assert.Equal(
      "Latest live result",
      fixture.CachedPlan(fixture.TruckA)!.Message
    );
    Assert.Equal(1, fixture.PlanningCalls);
    Assert.Equal(1, fixture.NextCalls);
  }

  [Fact]
  public async Task SwitchingTruckCancelsThePendingColdPreviewAndOnlyStartsPlanningForTheNewSelection()
  {
    using var fixture = new SelectionFixture(cacheCurrentPlans: false)
    {
      DeferPreview = true,
    };
    var component = fixture.Render();
    component.WaitForAssertion(
      () => Assert.Contains(fixture.Js.Calls, x => x.Name == "setTrucks")
    );
    await Toggle(component, "Next loads")
      .ChangeAsync(new ChangeEventArgs { Value = true });
    var selectA = component.InvokeAsync(
      () => component.Instance.OnTruckSelected(fixture.TruckA.ToString())
    );
    var previewA = await fixture.ReadPreviewAsync();
    var selectB = component.InvokeAsync(
      () => component.Instance.OnTruckSelected(fixture.TruckB.ToString())
    );
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
    Assert.Equal(
      fixture.TruckB,
      current.RootElement.GetProperty("truckId").GetGuid()
    );
  }

  [Fact]
  public async Task DisposingTheMapCancelsPendingPreviewWithoutStartingLivePlanning()
  {
    using var fixture = new SelectionFixture(cacheCurrentPlans: false)
    {
      DeferPreview = true,
    };
    var component = fixture.Render();
    component.WaitForAssertion(
      () => Assert.Contains(fixture.Js.Calls, x => x.Name == "setTrucks")
    );
    var selection = component.InvokeAsync(
      () => component.Instance.OnTruckSelected(fixture.TruckA.ToString())
    );
    var preview = await fixture.ReadPreviewAsync();
    await component.InvokeAsync(
      () => component.Instance.DisposeAsync().AsTask()
    );
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
  public async Task ReturningToTruckRestoresTheCompleteLatestSnapshotBeforeAnyNewHttpResponse(
    bool empty
  )
  {
    using var fixture = new SelectionFixture();
    var component = fixture.Render();
    component.WaitForAssertion(
      () => Assert.Contains(fixture.Js.Calls, x => x.Name == "setTrucks")
    );
    await Toggle(component, "Next loads")
      .ChangeAsync(new ChangeEventArgs { Value = true });
    var selectA = component.InvokeAsync(
      () => component.Instance.OnTruckSelected(fixture.TruckA.ToString())
    );
    var initialA = await fixture.ReadNextAsync();
    var future = fixture.FutureLoad(202);
    initialA.Reply("geometry-a", empty ? [] : [future]);
    await selectA;

    await Toggle(component, "Next loads")
      .ChangeAsync(new ChangeEventArgs { Value = false });
    var relabel = Toggle(component, "Next loads")
      .ChangeAsync(new ChangeEventArgs { Value = true });
    var labels = await fixture.ReadNextAsync();
    fixture.AssertIdentity(labels, fixture.TruckA, "geometry-a");
    labels.ReplyLabels(
      "labels-a",
      empty ? [] : [new(future.Id, ["Updated pickup", "Updated delivery"])]
    );
    await relabel;
    using (var delta = fixture.LastNextPayload())
      Assert.Equal(
        JsonValueKind.Null,
        delta.RootElement.GetProperty("routes").ValueKind
      );

    var selectB = component.InvokeAsync(
      () => component.Instance.OnTruckSelected(fixture.TruckB.ToString())
    );
    (await fixture.ReadNextAsync()).Reply("empty-b", []);
    await selectB;
    var published = fixture.Js.Calls.Count(x => x.Name == "setNextLoadsBytes");
    fixture.DeferPlanning = true;
    fixture.DeferDetails = true;
    var returnA = component.InvokeAsync(
      () => component.Instance.OnTruckSelected(fixture.TruckA.ToString())
    );
    var revalidate = await fixture.ReadNextAsync();
    var planning = await fixture.ReadPlanningAsync();
    var details = await fixture.ReadDetailsAsync();
    fixture.AssertIdentity(revalidate, fixture.TruckA, "labels-a");
    Assert.Equal(
      published + 1,
      fixture.Js.Calls.Count(x => x.Name == "setNextLoadsBytes")
    );
    using (var replay = fixture.LastNextPayload())
    {
      var routes = replay
        .RootElement.GetProperty("routes")
        .EnumerateArray()
        .ToArray();
      var names = replay
        .RootElement.GetProperty("labels")
        .EnumerateArray()
        .ToArray();
      if (empty)
      {
        Assert.Empty(routes);
        Assert.Empty(names);
      }
      else
      {
        Assert.Equal(
          future.Id,
          Assert.Single(routes).GetProperty("id").GetGuid()
        );
        Assert.Equal(
          2,
          routes[0]
            .GetProperty("legs")[0]
            .GetProperty("points")
            .GetArrayLength()
        );
        Assert.Equal(
          "Updated pickup",
          Assert.Single(names).GetProperty("names")[0].GetString()
        );
      }
    }
    Assert.False(returnA.IsCompleted);
    Assert.False(revalidate.Response.Task.IsCompleted);
    Assert.False(planning.Response.Task.IsCompleted);
    Assert.False(details.Response.Task.IsCompleted);
    revalidate.ReplyUnchanged("labels-a");
    planning.Reply(fixture.Plan(fixture.TruckA));
    details.Reply(
      new { id = fixture.Plan(fixture.TruckA).DispatchId, loadNumber = 1358 }
    );
    await returnA;
    Assert.Equal(4, fixture.NextCalls);
    Assert.Equal(
      published + 1,
      fixture.Js.Calls.Count(x => x.Name == "setNextLoadsBytes")
    );
  }

  private static IElement Toggle(
    IRenderedComponent<FleetMap> component,
    string label
  ) =>
    component
      .FindAll(".fleet-map-toggle")
      .Single(x => x.TextContent.Trim() == label)
      .QuerySelector("input")!;

  [Fact]
  public async Task FutureStopSelectionPinsItsDetailsAndReusesThemWithoutChangingTheCurrentRoute()
  {
    using var fixture = new SelectionFixture();
    var component = fixture.Render();
    component.WaitForAssertion(
      () => Assert.Contains(fixture.Js.Calls, x => x.Name == "setTrucks")
    );
    await component.InvokeAsync(
      () => component.Instance.OnTruckSelected(fixture.TruckA.ToString())
    );
    var toggle = Toggle(component, "Next loads")
      .ChangeAsync(new ChangeEventArgs { Value = true });
    var future = fixture.FutureLoad(202);
    (await fixture.ReadNextAsync()).Reply("future", [future]);
    await toggle;
    var current = fixture.Plan(fixture.TruckA).DispatchId!.Value.ToString();
    var planningCalls = fixture.PlanningCalls;
    var currentHeader = "";
    await component.InvokeAsync(() =>
    {
      currentHeader = component.Find("#fleet-map-details").TextContent;
    });
    fixture.DeferDetails = true;
    var selecting = component.InvokeAsync(
      () =>
        component.Instance.OnNextLoadSelected(
          fixture.TruckA.ToString(),
          current,
          future.Id.ToString(),
          0
        )
    );
    var request = await fixture.ReadDetailsAsync();
    Assert.Equal($"/api/dispatch/{future.Id}", request.Uri.AbsolutePath);
    component.WaitForAssertion(
      () =>
        Assert.Contains(
          "202",
          component.Find("[aria-label='Selected next load']").TextContent
        )
    );
    const string disclosure =
      "button[aria-controls='fleet-map-next-load-details']";
    var cardInstance = component.FindComponent<NextLoadDetailsCard>().Instance;
    Assert.False(cardInstance.Expanded);
    Assert.Equal(
      "false",
      component.Find(disclosure).GetAttribute("aria-expanded")
    );
    var mapCalls = fixture.Js.Calls.Count;
    var readsBeforeDisclosure = fixture.DetailsCalls;
    await component.Find(disclosure).ClickAsync(new());
    Assert.True(cardInstance.Expanded);
    Assert.Equal(mapCalls, fixture.Js.Calls.Count);
    Assert.Equal(readsBeforeDisclosure, fixture.DetailsCalls);
    Assert.Equal(planningCalls, fixture.PlanningCalls);
    request.Reply(
      new
      {
        id = future.Id,
        loadNumber = 202,
        orderNumber = "ORDER-FUTURE",
        customerName = "Future customer",
        stops = new[]
        {
          new
          {
            id = future.Stops[0].Id,
            sequence = 1,
            job = "Pick Up",
            name = "Future pickup",
            address = "10 First St",
            city = "Toronto",
            province = "ON",
            zipCode = "M5V 3A8",
            country = "Canada",
            scheduledDate = "2026-09-09",
            scheduledTime = "09:00:00",
            scheduledDate2 = "2026-09-09",
            scheduledTime2 = "14:00:00",
            commodity = "Machinery",
            notes = "Service for Load pickup",
          },
          new
          {
            id = future.Stops[1].Id,
            sequence = 2,
            job = "Drop Off",
            name = "Future delivery",
            address = "20 Second St",
            city = "Chicago",
            province = "IL",
            zipCode = "60607",
            country = "US",
            scheduledDate = "2026-09-10",
            scheduledTime = "14:00:00",
            scheduledDate2 = "2026-09-10",
            scheduledTime2 = "16:00:00",
            commodity = "Machinery",
            notes = "Service for Load delivery",
          },
        },
      }
    );
    await selecting;
    Assert.Same(
      cardInstance,
      component.FindComponent<NextLoadDetailsCard>().Instance
    );
    Assert.True(cardInstance.Expanded);
    var details = component
      .Find("[aria-label='Selected next load']")
      .TextContent;
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
      var card = component.Find(
        ".fleet-map-stage [aria-label='Selected next load']"
      );
      Assert.True(card.ClassList.Contains("fleet-map-inspector__next"));
      Assert.False(card.ClassList.Contains("fleet-map-details-card"));
      Assert.Equal("region", card.GetAttribute("role"));
      Assert.NotNull(card.Closest(".fleet-map-inspector"));
      var location = card.QuerySelector(".fleet-route-popup__location")!;
      Assert.True(
        location.FirstElementChild!.ClassList.Contains(
          "fleet-map-next-load-card__header"
        )
      );
      Assert.Empty(location.QuerySelectorAll("a"));
      Assert.Equal(
        $"/dispatch/{future.Id}",
        card.QuerySelector(
              ".fleet-route-popup__information > .fleet-route-popup__details-link"
            )!
          .GetAttribute("href")
      );
      Assert.Matches(
        @"Appointment\s*Sep 9\s*·\s*09:00 AM\s*–\s*02:00 PM",
        card.QuerySelector(".fleet-route-popup__information")!.TextContent
      );
      Assert.DoesNotContain(
        "Appointment",
        card.QuerySelector(".fleet-route-popup__location")!.TextContent
      );
      Assert.Equal(
        new[] { "10 First St", "Toronto, ON M5V 3A8, Canada" },
        card.QuerySelectorAll(".fleet-route-popup__address-line")
          .Select(x => x.TextContent)
      );
      Assert.True(
        card.QuerySelector(".fleet-route-popup__information")!
          .TextContent.IndexOf("Appointment", StringComparison.Ordinal)
          < card.QuerySelector(".fleet-route-popup__information")!
            .TextContent.IndexOf("ETA", StringComparison.Ordinal)
      );
      Assert.Single(Regex.Matches(card.TextContent, "Appointment"));
      Assert.Empty(
        component.FindAll(
          "#fleet-map-details [aria-label='Selected next load']"
        )
      );
      Assert.Single(
        component.FindAll(
          "#fleet-map-details [aria-label='Current dispatch route']"
        )
      );
      Assert.Equal(
        currentHeader,
        component.Find("#fleet-map-details").TextContent
      );
      Assert.True(component.Find("#fleet-map-details").HasAttribute("hidden"));
      Assert.Contains(
        "Next load stop",
        component.Find(".fleet-map-inspector__header").TextContent
      );
      Assert.Empty(component.FindAll(".fleet-map-details-card"));
      Assert.Empty(component.FindAll("#fleet-map-details a"));
      Assert.Contains(
        "Back to truck",
        component.Find(".fleet-map-inspector__header").TextContent
      );
      Assert.Contains(
        card.QuerySelectorAll("a"),
        link => link.GetAttribute("href") == $"/dispatch/{future.Id}"
      );
    });
    using (var route = fixture.LastCurrentPayload())
      Assert.Equal(
        fixture.Plan(fixture.TruckA).State!.Plan!.Id,
        route.RootElement.GetProperty("id").GetGuid()
      );
    var detailCalls = fixture.DetailsCalls;
    await component.InvokeAsync(
      () =>
        component.Instance.OnNextLoadSelected(
          fixture.TruckA.ToString(),
          current,
          future.Id.ToString(),
          1
        )
    );
    Assert.Contains(
      "Future delivery",
      component.Find("[aria-label='Selected next load']").TextContent
    );
    Assert.False(
      component.FindComponent<NextLoadDetailsCard>().Instance.Expanded
    );
    await component.Find(disclosure).ClickAsync(new());
    Assert.True(
      component.FindComponent<NextLoadDetailsCard>().Instance.Expanded
    );
    await component.Find(disclosure).ClickAsync(new());
    Assert.False(
      component.FindComponent<NextLoadDetailsCard>().Instance.Expanded
    );
    Assert.Contains(
      "Sep 10",
      component.Find("[aria-label='Selected next load']").TextContent
    );
    Assert.DoesNotContain(
      "Machinery",
      component.Find("[aria-label='Selected next load']").TextContent
    );
    Assert.DoesNotContain(
      "Service for Load",
      component.Find("[aria-label='Selected next load']").TextContent
    );
    Assert.Equal(detailCalls, fixture.DetailsCalls);
    await component.InvokeAsync(
      () =>
        component.Instance.OnNextLoadSelected(
          fixture.TruckA.ToString(),
          current,
          null,
          0
        )
    );
    Assert.Single(component.FindAll("[aria-label='Current dispatch route']"));
    await component.InvokeAsync(
      () =>
        component.Instance.OnNextLoadSelected(
          fixture.TruckA.ToString(),
          current,
          future.Id.ToString(),
          0
        )
    );
    Assert.Equal(detailCalls, fixture.DetailsCalls);
    await component.InvokeAsync(
      () => component.Instance.OnTruckSelected(fixture.TruckA.ToString())
    );
    Assert.Empty(component.FindAll("[aria-label='Selected next load']"));
    Assert.Single(component.FindAll("[aria-label='Current dispatch route']"));
    Assert.Equal(planningCalls, fixture.PlanningCalls);
  }

  [Theory]
  [InlineData(false)]
  [InlineData(true)]
  public async Task ClosingFutureDetailsCancelsPendingReplyWithoutChangingTheCurrentSelection(
    bool escape
  )
  {
    using var fixture = new SelectionFixture();
    var component = fixture.Render();
    component.WaitForAssertion(
      () => Assert.Contains(fixture.Js.Calls, x => x.Name == "setTrucks")
    );
    await component.InvokeAsync(
      () => component.Instance.OnTruckSelected(fixture.TruckA.ToString())
    );
    var toggle = component.InvokeAsync(
      () =>
        Toggle(component, "Next loads")
          .ChangeAsync(new ChangeEventArgs { Value = true })
    );
    var future = fixture.FutureLoad(202);
    (await fixture.ReadNextAsync()).Reply("future", [future]);
    await toggle;
    var current = fixture.Plan(fixture.TruckA).DispatchId!.Value.ToString();
    var currentHeader = "";
    await component.InvokeAsync(() =>
    {
      currentHeader = component.Find("#fleet-map-details").TextContent;
    });
    fixture.DeferDetails = true;
    var selecting = component.InvokeAsync(
      () =>
        component.Instance.OnNextLoadSelected(
          fixture.TruckA.ToString(),
          current,
          future.Id.ToString(),
          0
        )
    );
    var pending = await fixture.ReadDetailsAsync();
    var httpCalls = fixture.HttpCalls;
    var mapCalls = fixture.Js.Calls.Count;

    await component.InvokeAsync(async () =>
    {
      var card = component.Find(
        ".fleet-map-stage [aria-label='Selected next load']"
      );
      Assert.NotNull(
        card.QuerySelector(".loading-slot.is-loading .appearance-loader")
      );
      Assert.DoesNotContain("Loading load details", card.TextContent);
      if (escape)
        await component
          .Find(".fleet-map-inspector")
          .KeyDownAsync(new KeyboardEventArgs { Key = "Escape" });
      else
        await component
          .Find(".fleet-map-inspector__back")
          .ClickAsync(new MouseEventArgs());
      Assert.Empty(component.FindAll("[aria-label='Selected next load']"));
      Assert.Equal(
        currentHeader,
        component.Find("#fleet-map-details").TextContent
      );
      Assert.True(Toggle(component, "Next loads").HasAttribute("checked"));
      AssertTruckPanelsVisible(component);
    });
    Assert.True(pending.Cancellation.IsCancellationRequested);
    Assert.Equal(httpCalls, fixture.HttpCalls);
    var transition = Assert.Single(fixture.Js.Calls.Skip(mapCalls));
    Assert.Equal("setInspectorMode", transition.Name);
    Assert.Equal("truck", transition.Args![0]);

    pending.Reply(
      new
      {
        id = future.Id,
        loadNumber = 202,
        customerName = "Obsolete closed details",
      }
    );
    await selecting;
    await component.InvokeAsync(() =>
    {
      Assert.Empty(component.FindAll("[aria-label='Selected next load']"));
      Assert.DoesNotContain("Obsolete closed details", component.Markup);
      Assert.Equal(
        currentHeader,
        component.Find("#fleet-map-details").TextContent
      );
    });
    Assert.Equal(httpCalls, fixture.HttpCalls);
    using var route = fixture.LastCurrentPayload();
    Assert.Equal(
      fixture.Plan(fixture.TruckA).State!.Plan!.Id,
      route.RootElement.GetProperty("id").GetGuid()
    );
  }

  [Fact]
  public async Task LateFutureDetailsCannotReplaceThePanelAfterChangingTrucks()
  {
    using var fixture = new SelectionFixture();
    var component = fixture.Render();
    component.WaitForAssertion(
      () => Assert.Contains(fixture.Js.Calls, x => x.Name == "setTrucks")
    );
    await component.InvokeAsync(
      () => component.Instance.OnTruckSelected(fixture.TruckA.ToString())
    );
    var toggle = Toggle(component, "Next loads")
      .ChangeAsync(new ChangeEventArgs { Value = true });
    var future = fixture.FutureLoad(202);
    (await fixture.ReadNextAsync()).Reply("future", [future]);
    await toggle;
    fixture.DeferDetails = true;
    var selecting = component.InvokeAsync(
      () =>
        component.Instance.OnNextLoadSelected(
          fixture.TruckA.ToString(),
          fixture.Plan(fixture.TruckA).DispatchId!.Value.ToString(),
          future.Id.ToString(),
          0
        )
    );
    var details = await fixture.ReadDetailsAsync();
    fixture.DeferDetails = false;
    var otherTruck = component.InvokeAsync(
      () => component.Instance.OnTruckSelected(fixture.TruckB.ToString())
    );
    (await fixture.ReadNextAsync()).Reply("empty-b", []);
    await otherTruck;
    Assert.True(details.Cancellation.IsCancellationRequested);
    details.Reply(
      new
      {
        id = future.Id,
        loadNumber = 202,
        customerName = "Obsolete future details",
      }
    );
    await selecting;
    Assert.DoesNotContain("Obsolete future details", component.Markup);
    Assert.Empty(component.FindAll("[aria-label='Selected next load']"));
    Assert.Contains(
      "64888",
      component.Find(".fleet-map-inspector__header").TextContent
    );
    using var route = fixture.LastCurrentPayload();
    Assert.Equal(
      fixture.TruckB,
      route.RootElement.GetProperty("truckId").GetGuid()
    );
  }

  [Theory]
  [InlineData(false)]
  [InlineData(true)]
  public async Task FuturePanelUsesOnlyTheSelectedStopFreshEtaFromSavedDetailsOrTheCurrentChain(
    bool fromChain
  )
  {
    using var fixture = new SelectionFixture();
    var future = fixture.FutureLoad(202);
    var pickupId = future.Stops[0].Id;
    var deliveryId = future.Stops[1].Id;
    var now = fixture.Clock.GetUtcNow().UtcDateTime;
    var arrival = new DateTimeOffset(
      2026,
      9,
      10,
      14,
      0,
      0,
      TimeSpan.FromHours(-4)
    );
    var eta = new DispatchEta(
      now,
      now.AddMinutes(2),
      [
        new(
          deliveryId,
          arrival.AddHours(-5),
          "America/Toronto",
          null,
          null,
          20,
          0
        )
        {
          DispatchId = Guid.NewGuid(),
        },
        new(
          pickupId,
          arrival.AddHours(-3),
          "America/Toronto",
          null,
          null,
          20,
          0
        )
        {
          DispatchId = future.Id,
        },
        new(deliveryId, arrival, "America/Toronto", null, null, 60, 0)
        {
          DispatchId = future.Id,
        },
      ],
      null,
      []
    );
    if (fromChain)
      fixture.SetEta(fixture.TruckA, eta);
    var component = fixture.Render();
    component.WaitForAssertion(
      () => Assert.Contains(fixture.Js.Calls, x => x.Name == "setTrucks")
    );
    await component.InvokeAsync(
      () => component.Instance.OnTruckSelected(fixture.TruckA.ToString())
    );
    var toggle = Toggle(component, "Next loads")
      .ChangeAsync(new ChangeEventArgs { Value = true });
    (await fixture.ReadNextAsync()).Reply("future", [future]);
    await toggle;
    var current = fixture.Plan(fixture.TruckA).DispatchId!.Value.ToString();
    fixture.DeferDetails = true;
    var selecting = component.InvokeAsync(
      () =>
        component.Instance.OnNextLoadSelected(
          fixture.TruckA.ToString(),
          current,
          future.Id.ToString(),
          1
        )
    );
    var request = await fixture.ReadDetailsAsync();
    request.Reply(
      new DispatchResponse
      {
        Id = future.Id,
        LoadNumber = future.LoadNumber,
        Eta = fromChain ? null : eta,
        Stops =
        [
          new()
          {
            Id = pickupId,
            Sequence = 1,
            Job = "Pick Up",
            Name = "Pickup",
          },
          new()
          {
            Id = deliveryId,
            Sequence = 2,
            Job = "Drop Off",
            Name = "Delivery",
          },
        ],
      }
    );
    await selecting;
    var details = component
      .Find("[aria-label='Selected next load']")
      .TextContent;
    Assert.Contains("ETA", details);
    Assert.Contains("02:00 PM", details);
    Assert.DoesNotContain("11:00 AM", details);
    Assert.DoesNotContain("09:00 AM", details);
    var inspected = component.Find("[aria-label='Selected next load']");
    Assert.Single(Regex.Matches(inspected.TextContent, "Appointment"));
    Assert.DoesNotContain(
      "Appointment",
      inspected.QuerySelector(".fleet-route-popup__location")!.TextContent
    );
    Assert.Contains(
      "Appointment",
      inspected.QuerySelector(".fleet-route-popup__information")!.TextContent
    );
    Assert.Contains(
      "02:00 PM",
      inspected.QuerySelector(".fleet-route-popup__information")!.TextContent
    );
    var calls = fixture.DetailsCalls;
    var etaPublications = fixture.Js.Calls.Count(x => x.Name == "setStopEtas");
    await component.InvokeAsync(
      () => fixture.Clock.Advance(TimeSpan.FromMinutes(3))
    );
    (await fixture.ReadNextAsync()).ReplyUnchanged("future");
    component.WaitForAssertion(() =>
    {
      Assert.True(
        fixture.Js.Calls.Count(x => x.Name == "setStopEtas") > etaPublications
      );
      using var payload = JsonSerializer.SerializeToDocument(
        fixture.Js.Calls.Last(x => x.Name == "setStopEtas").Args![0]
      );
      Assert.False(payload.RootElement.GetProperty("Refreshing").GetBoolean());
    });
    await component.InvokeAsync(
      () =>
        component.Instance.OnNextLoadSelected(
          fixture.TruckA.ToString(),
          current,
          future.Id.ToString(),
          0
        )
    );
    var pickupDetails = component
      .Find("[aria-label='Selected next load']")
      .TextContent;
    if (fromChain)
    {
      Assert.Contains("11:00 AM", pickupDetails);
      Assert.DoesNotContain("02:00 PM", pickupDetails);
      Assert.DoesNotContain("09:00 AM", pickupDetails);
    }
    else
      Assert.Matches(@"ETA\s*—", pickupDetails);
    Assert.Equal(calls, fixture.DetailsCalls);
    if (fromChain)
    {
      await component.InvokeAsync(
        () => fixture.Clock.Advance(TimeSpan.FromMinutes(14))
      );
      (await fixture.ReadNextAsync()).ReplyUnchanged("future");
      component.Render();
      component.WaitForAssertion(
        () =>
          Assert.Matches(
            @"ETA\s*—",
            component.Find("[aria-label='Selected next load']").TextContent
          )
      );
      Assert.Equal(calls, fixture.DetailsCalls);
    }
  }

  [Fact]
  public async Task PendingCurrentRefreshRetainsTheDisplayedStopButCannotGrantGraceToAnotherSavedStop()
  {
    using var fixture = new SelectionFixture();
    var future = fixture.FutureLoad(202);
    var now = fixture.Clock.GetUtcNow().UtcDateTime;
    var eta = new DispatchEta(
      now,
      now.AddMinutes(2),
      future
        .Stops.Select(
          (stop, index) =>
            new StopEta(
              stop.Id,
              now.AddHours(index + 1),
              "UTC",
              null,
              null,
              60,
              0
            )
            {
              DispatchId = future.Id,
            }
        )
        .ToArray(),
      null,
      []
    );
    var component = fixture.Render();
    component.WaitForAssertion(
      () => Assert.Contains(fixture.Js.Calls, x => x.Name == "setTrucks")
    );
    await component.InvokeAsync(
      () => component.Instance.OnTruckSelected(fixture.TruckA.ToString())
    );
    var toggle = component.InvokeAsync(
      () =>
        Toggle(component, "Next loads")
          .ChangeAsync(new ChangeEventArgs { Value = true })
    );
    (await fixture.ReadNextAsync()).Reply("future", [future]);
    await toggle;
    var current = fixture.Plan(fixture.TruckA).DispatchId!.Value.ToString();
    fixture.DeferDetails = true;
    var selecting = component.InvokeAsync(
      () =>
        component.Instance.OnNextLoadSelected(
          fixture.TruckA.ToString(),
          current,
          future.Id.ToString(),
          1
        )
    );
    (await fixture.ReadDetailsAsync()).Reply(
      new DispatchResponse
      {
        Id = future.Id,
        Eta = eta,
        Stops = future
          .Stops.Select(
            (stop, index) =>
              new DispatchStopResponse
              {
                Id = stop.Id,
                Sequence = index + 1,
                Job = index == 0 ? "Pickup" : "Delivery",
              }
          )
          .ToList(),
      }
    );
    await selecting;
    var shown = component
      .Find("[aria-label='Selected next load'] .arrival-estimate")
      .OuterHtml;
    var detailsReads = fixture.DetailsCalls;
    fixture.DeferPlanning = true;
    await component.InvokeAsync(
      () => fixture.Clock.Advance(TimeSpan.FromMinutes(3))
    );
    var pending = await fixture.ReadPlanningAsync();
    var next = await fixture.ReadNextAsync();
    component.WaitForAssertion(
      () =>
        Assert.True(
          component.FindComponent<NextLoadDetailsCard>().Instance.Refreshing
        )
    );
    Assert.Equal(
      shown,
      component
        .Find("[aria-label='Selected next load'] .arrival-estimate")
        .OuterHtml
    );
    await component.InvokeAsync(
      () =>
        component.Instance.OnNextLoadSelected(
          fixture.TruckA.ToString(),
          current,
          future.Id.ToString(),
          0
        )
    );
    Assert.Null(component.FindComponent<NextLoadDetailsCard>().Instance.Eta);
    Assert.Matches(
      @"ETA\s*—",
      component.Find("[aria-label='Selected next load']").TextContent
    );
    Assert.Equal(detailsReads, fixture.DetailsCalls);
    pending.Reply(fixture.Plan(fixture.TruckA));
    next.ReplyUnchanged("future");
    component.WaitForAssertion(
      () =>
        Assert.False(
          component.FindComponent<NextLoadDetailsCard>().Instance.Refreshing
        )
    );
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
      Legs = [new(30, 1800, [new(40, -80), new(41, -79)])],
    };
    var second = fixture.FutureLoad(303) with
    {
      Deadhead = new(20, []),
      Legs = [new(40, 2400, [new(41, -79), new(42, -78)])],
    };
    var component = fixture.Render();
    component.WaitForAssertion(
      () => Assert.Contains(fixture.Js.Calls, x => x.Name == "setTrucks")
    );
    await component.InvokeAsync(
      () => component.Instance.OnTruckSelected(fixture.TruckA.ToString())
    );
    await component.InvokeAsync(
      () =>
        component.Instance.OnRouteProgress(fixture.TruckA.ToString(), 90, 10)
    );
    var toggle = component.InvokeAsync(
      () =>
        Toggle(component, "Next loads")
          .ChangeAsync(new ChangeEventArgs { Value = true })
    );
    (await fixture.ReadNextAsync()).Reply("future", [first, second]);
    await toggle;
    var current = plan.DispatchId.ToString();

    foreach (
      var (load, stop, expected, leg) in new[]
      {
        (first, 0, 105d, "15\u00a0mi · 24\u00a0km"),
        (first, 1, 135d, "30\u00a0mi · 48\u00a0km"),
        (second, 0, 155d, "20\u00a0mi · 32\u00a0km"),
        (second, 1, 195d, "40\u00a0mi · 64\u00a0km"),
      }
    )
    {
      await component.InvokeAsync(
        () =>
          component.Instance.OnNextLoadSelected(
            fixture.TruckA.ToString(),
            current,
            load.Id.ToString(),
            stop
          )
      );
      await component.InvokeAsync(() =>
      {
        Assert.Equal(
          (double?)expected,
          component.FindComponent<NextLoadDetailsCard>().Instance.DistanceMiles
        );
        var metrics = component.Find(
          "[aria-label='Selected next load'] .fleet-route-popup__information .fleet-map-next-load-card__metrics"
        );
        Assert.Equal(
          new[] { stop == 0 ? "Empty" : "Leg", "Total" },
          metrics.QuerySelectorAll("dt").Select(x => x.TextContent)
        );
        Assert.Equal(leg, metrics.QuerySelector("dd")!.TextContent);
        Assert.DoesNotContain("Load Distance", metrics.TextContent);
      });
    }
    await component.InvokeAsync(() =>
    {
      var card = component
        .Find("[aria-label='Selected next load']")
        .TextContent;
      Assert.Contains("195\u00a0mi", card);
      Assert.Contains("314\u00a0km", card);
      Assert.Contains("Appointment", card);
      Assert.Matches(@"ETA\s*—", card);
    });
    var calls = fixture.HttpCalls;
    await component.InvokeAsync(
      () =>
        component.Instance.OnRouteProgress(fixture.TruckA.ToString(), 80, 20)
    );
    await component.InvokeAsync(
      () =>
        Assert.Equal(
          (double?)185d,
          component.FindComponent<NextLoadDetailsCard>().Instance.DistanceMiles
        )
    );
    Assert.Equal(calls, fixture.HttpCalls);
  }

  [Theory]
  [InlineData("missing-eta")]
  [InlineData("empty-eta")]
  [InlineData("missing-plan")]
  public async Task RecalculationRetainsTheSameEtaAndDisplayedDistanceInHeaderFutureCardAndMap(
    string missing
  )
  {
    using var fixture = new SelectionFixture();
    var now = fixture.Clock.GetUtcNow().UtcDateTime;
    var plan = fixture.Plan(fixture.TruckA).State!.Plan!;
    var currentStop = new PlanStop(
      Guid.NewGuid(),
      "Current delivery",
      "123 Main Street",
      1,
      new(40, -80)
    );
    plan.Stops = [currentStop];
    plan.Tracking.NextStopId = currentStop.Id;
    var future = fixture.FutureLoad(202) with { Deadhead = new(15, []) };
    var forecast = new DispatchEta(
      now,
      now.AddMinutes(2),
      [
        new(currentStop.Id, now.AddHours(1), "UTC", null, 0, 60, 0)
        {
          DispatchId = plan.DispatchId,
        },
        new(future.Stops[1].Id, now.AddHours(2), "UTC", null, 0, 120, 0)
        {
          DispatchId = future.Id,
        },
      ],
      null,
      []
    );
    fixture.SetEta(fixture.TruckA, forecast);
    var saved = fixture.Plan(fixture.TruckA);
    var component = fixture.Render();
    component.WaitForAssertion(
      () => Assert.Contains(fixture.Js.Calls, x => x.Name == "setTrucks")
    );
    await component.InvokeAsync(
      () => component.Instance.OnTruckSelected(fixture.TruckA.ToString())
    );
    await component.InvokeAsync(
      () =>
        component.Instance.OnRouteProgress(fixture.TruckA.ToString(), 90, 10)
    );
    var toggle = component.InvokeAsync(
      () =>
        Toggle(component, "Next loads")
          .ChangeAsync(new ChangeEventArgs { Value = true })
    );
    (await fixture.ReadNextAsync()).Reply("future", [future]);
    await toggle;
    await component.InvokeAsync(
      () =>
        component.Instance.OnNextLoadSelected(
          fixture.TruckA.ToString(),
          plan.DispatchId.ToString(),
          future.Id.ToString(),
          1
        )
    );
    Assert.Equal(
      115,
      component.FindComponent<NextLoadDetailsCard>().Instance.DistanceMiles
    );
    var previousCurrentEta = component
      .Find("[aria-label='Current dispatch route'] .arrival-estimate")
      .OuterHtml;
    var previousFutureEta = component
      .Find("[aria-label='Selected next load'] .arrival-estimate")
      .OuterHtml;

    var pending = saved with
    {
      State = saved.State! with
      {
        Plan = missing == "missing-plan" ? null : saved.State!.Plan,
        Eta =
          missing == "missing-eta"
            ? null
            : forecast with
            {
              Stops = [],
              RouteUpdatePending = true,
            },
      },
    };
    fixture.SetPlanningResult(fixture.TruckA, pending);
    // One position poll. Upcoming loads were checked moments ago, so this
    // beat refreshes the route alone.
    await component.InvokeAsync(
      () => fixture.Clock.Advance(TimeSpan.FromSeconds(10))
    );
    component.WaitForAssertion(() =>
    {
      Assert.True(
        component
          .FindComponent<NextLoadDetailsCard>()
          .Instance.Eta?.RouteUpdatePending
      );
      using var payload = JsonSerializer.SerializeToDocument(
        fixture.Js.Calls.Last(x => x.Name == "setStopEtas").Args![0]
      );
      Assert.False(payload.RootElement.GetProperty("Refreshing").GetBoolean());
      Assert.IsType<RouteProgress>(
        fixture.Js.Calls.Last(x => x.Name == "setRouteBytes").Args![1]
      );
    });
    await component.InvokeAsync(
      () =>
        component.Instance.OnRouteProgress(fixture.TruckA.ToString(), 80, 20)
    );
    Assert.Equal(
      115,
      component.FindComponent<NextLoadDetailsCard>().Instance.DistanceMiles
    );
    Assert.Equal(
      previousCurrentEta,
      component
        .Find("[aria-label='Current dispatch route'] .arrival-estimate")
        .OuterHtml
    );
    Assert.Equal(
      previousFutureEta,
      component
        .Find("[aria-label='Selected next load'] .arrival-estimate")
        .OuterHtml
    );
    Assert.Equal(2, component.FindAll(".arrival-estimate__ontime").Count);
    Assert.DoesNotContain("Updating", component.Markup);
    var progress = Assert.IsType<RouteProgress>(
      fixture.Js.Calls.Last(x => x.Name == "setRouteBytes").Args![1]
    );
    Assert.Equal(90, progress.RemainingMiles);
    using var published = JsonSerializer.SerializeToDocument(
      fixture.Js.Calls.Last(x => x.Name == "setStopEtas").Args![0]
    );
    var publishedEta = published
      .RootElement.GetProperty("Eta")
      .Deserialize<DispatchEta>()!;
    Assert.Equal(forecast.Stops, publishedEta.Stops);
    Assert.Equal(forecast.ValidUntil, publishedEta.ValidUntil);
    Assert.True(publishedEta.RouteUpdatePending);

    await component.InvokeAsync(
      () => fixture.Clock.Advance(TimeSpan.FromMinutes(17))
    );
    (await fixture.ReadNextAsync()).ReplyUnchanged("future");
    component.WaitForAssertion(
      () =>
        Assert.Null(component.FindComponent<NextLoadDetailsCard>().Instance.Eta)
    );
    Assert.Null(
      component.FindComponent<NextLoadDetailsCard>().Instance.DistanceMiles
    );
    var refreshedAt = fixture.Clock.GetUtcNow().UtcDateTime;
    var fresh = forecast with
    {
      CalculatedAt = refreshedAt,
      ValidUntil = refreshedAt.AddMinutes(2),
      Stops = forecast
        .Stops.Select(stop =>
          stop with
          {
            Arrival = stop.Arrival.AddMinutes(5),
          }
        )
        .ToArray(),
    };
    fixture.SetPlanningResult(
      fixture.TruckA,
      saved with
      {
        State = saved.State! with { Eta = fresh },
      }
    );
    // One position poll. Upcoming loads were checked moments ago, so this
    // beat refreshes the route alone.
    await component.InvokeAsync(
      () => fixture.Clock.Advance(TimeSpan.FromSeconds(10))
    );
    component.WaitForAssertion(() =>
    {
      Assert.Equal(
        fresh.Stops[1].Arrival,
        component
          .FindComponent<NextLoadDetailsCard>()
          .Instance.Eta?.Stops[0]
          .Arrival
      );
      Assert.DoesNotContain(
        "Updating",
        component.Find("[aria-label='Selected next load']").TextContent
      );
    });
    await component.InvokeAsync(
      () =>
        component.Instance.OnRouteProgress(fixture.TruckA.ToString(), 80, 20)
    );
    Assert.Equal(
      105,
      component.FindComponent<NextLoadDetailsCard>().Instance.DistanceMiles
    );
    Assert.Equal(
      fresh.Stops[1].Arrival,
      component
        .FindComponent<NextLoadDetailsCard>()
        .Instance.Eta!.Stops[0]
        .Arrival
    );
  }

  [Theory]
  [InlineData("truck")]
  [InlineData("dispatch")]
  [InlineData("next-stop")]
  [InlineData("not-pending")]
  public async Task MissingPlanCannotReuseAPreviousRouteForDifferentOrUnavailableOwnership(
    string changed
  )
  {
    using var fixture = new SelectionFixture();
    var now = fixture.Clock.GetUtcNow().UtcDateTime;
    var saved = fixture.Plan(fixture.TruckA);
    var stop = new PlanStop(
      Guid.NewGuid(),
      "Delivery",
      "Warehouse",
      1,
      new(40, -80)
    );
    saved.State!.Plan!.Stops = [stop];
    saved.State.Plan.Tracking.NextStopId = stop.Id;
    var eta = new DispatchEta(
      now,
      now.AddMinutes(2),
      [
        new(stop.Id, now.AddHours(1), "UTC", null, 0, 60, 0)
        {
          DispatchId = saved.DispatchId!.Value,
        },
      ],
      null,
      []
    );
    fixture.SetEta(fixture.TruckA, eta);
    var component = fixture.Render();
    component.WaitForAssertion(
      () => Assert.Contains(fixture.Js.Calls, x => x.Name == "setTrucks")
    );
    await component.InvokeAsync(
      () => component.Instance.OnTruckSelected(fixture.TruckA.ToString())
    );
    Assert.Single(component.FindAll("[aria-label='Current dispatch route']"));
    var pending = saved with
    {
      TruckId = changed == "truck" ? fixture.TruckB : saved.TruckId,
      DispatchId = changed == "dispatch" ? Guid.NewGuid() : saved.DispatchId,
      State = saved.State with
      {
        Plan = null,
        Eta = eta with
        {
          RouteUpdatePending = changed != "not-pending",
          Stops =
            changed == "next-stop"
              ? [eta.Stops[0] with { StopId = Guid.NewGuid() }]
              : [],
        },
      },
    };
    fixture.SetPlanningResult(fixture.TruckA, pending);
    await component.InvokeAsync(
      () => fixture.Clock.Advance(TimeSpan.FromSeconds(10))
    );
    component.WaitForAssertion(() =>
    {
      var panel = component.Find("[aria-label='Current dispatch route']");
      Assert.Single(panel.QuerySelectorAll(".fleet-map-route-info__metric"));
      Assert.Equal(
        "— mi",
        panel
          .QuerySelector(".fleet-map-route-info__distance strong")!
          .TextContent
      );
      Assert.All(
        panel.QuerySelectorAll(".fleet-map-route-info__metric strong"),
        value => Assert.Equal("— mi", value.TextContent)
      );
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
  public async Task UnknownFutureDistanceDoesNotShowPartialTotals(
    string unknown
  )
  {
    using var fixture = new SelectionFixture();
    var first = fixture.FutureLoad(202) with
    {
      Deadhead = unknown == "deadhead" ? null : new(15, []),
      Legs =
        unknown == "leg" ? [] : [new(30, 1800, [new(40, -80), new(41, -79)])],
    };
    var second = fixture.FutureLoad(303) with { Deadhead = new(20, []) };
    var component = fixture.Render();
    component.WaitForAssertion(
      () => Assert.Contains(fixture.Js.Calls, x => x.Name == "setTrucks")
    );
    await component.InvokeAsync(
      () => component.Instance.OnTruckSelected(fixture.TruckA.ToString())
    );
    if (unknown != "remaining")
      await component.InvokeAsync(
        () =>
          component.Instance.OnRouteProgress(fixture.TruckA.ToString(), 90, 10)
      );
    var toggle = component.InvokeAsync(
      () =>
        Toggle(component, "Next loads")
          .ChangeAsync(new ChangeEventArgs { Value = true })
    );
    (await fixture.ReadNextAsync()).Reply("future", [first, second]);
    await toggle;
    await component.InvokeAsync(
      () =>
        component.Instance.OnNextLoadSelected(
          fixture.TruckA.ToString(),
          fixture.Plan(fixture.TruckA).DispatchId!.Value.ToString(),
          second.Id.ToString(),
          1
        )
    );
    await component.InvokeAsync(() =>
    {
      Assert.Null(
        component.FindComponent<NextLoadDetailsCard>().Instance.DistanceMiles
      );
      var information = component
        .Find(
          "[aria-label='Selected next load'] .fleet-route-popup__information"
        )
        .TextContent;
      Assert.Matches(@"Total\s*—", information);
      Assert.Matches(@"Leg\s*10\u00a0mi\s*·\s*16\u00a0km", information);
    });
  }

  [Fact]
  public void FutureDetailsShowTheSelectedStopsPrecedingLegInsteadOfAllLoadedMiles()
  {
    using var context = new BunitContext();
    context.Services.AddSingleton<TimeProvider>(new FakeTimeProvider());
    var route = new NextLoadRoute(
      Guid.NewGuid(),
      202,
      "ready",
      [new(10, 600, []), new(20, 1200, []), new(30, 1800, [])],
      [
        new(40, -80, "Pickup"),
        new(41, -79, "Delivery"),
        new(42, -78, "Delivery"),
        new(43, -77, "Delivery"),
      ],
      new(15, [])
    );
    var component = context.Render<NextLoadDetailsCard>(p =>
      p.Add(x => x.Route, route)
        .Add(x => x.StopIndex, 2)
        .Add(x => x.DistanceMiles, 105d)
    );
    var metrics = component.Find(".fleet-map-next-load-card__metrics");
    Assert.Equal(
      new[] { "Leg", "Total" },
      metrics.QuerySelectorAll("dt").Select(x => x.TextContent)
    );
    Assert.Equal(
      new[] { "20\u00a0mi · 32\u00a0km", "105\u00a0mi · 169\u00a0km" },
      metrics.QuerySelectorAll("dd").Select(x => x.TextContent)
    );
    component.Render(p => p.Add(x => x.StopIndex, 4));
    Assert.Equal(
      "—",
      component.Find(".fleet-map-next-load-card__metrics dd").TextContent
    );
  }

  [Fact]
  public async Task FutureDetailsSplitAddressForDisplayButCopyTheOriginalWholeValue()
  {
    using var context = new BunitContext();
    context.Services.AddSingleton<TimeProvider>(new FakeTimeProvider());
    var route = new NextLoadRoute(Guid.NewGuid(), 1373, "ready", [], []);
    var stop = new PlanStop(
      Guid.NewGuid(),
      "Warehouse",
      " 308 Springhill Farm Rd, building 3, Fort Mill, SC 29715, US ",
      1,
      new(40, -80)
    );
    string? copied = null;
    var component = context.Render<NextLoadDetailsCard>(p =>
      p.Add(x => x.Route, route)
        .Add(x => x.Stop, stop)
        .Add(x => x.OnCopy, value => copied = value)
    );
    Assert.Equal(
      new[] { "308 Springhill Farm Rd, building 3", "Fort Mill, SC 29715, US" },
      component
        .FindAll(".fleet-route-popup__address-line")
        .Select(x => x.TextContent)
    );
    await component
      .Find("[title='Copy full address']")
      .ClickAsync(new MouseEventArgs());
    Assert.Equal(stop.Address, copied);

    var changed = stop with { Address = "530 Henry St, Rome, NY, 13440, US" };
    component.Render(p => p.Add(x => x.Stop, changed));
    Assert.Equal(
      new[] { "530 Henry St", "Rome, NY 13440, US" },
      component
        .FindAll(".fleet-route-popup__address-line")
        .Select(x => x.TextContent)
    );
    await component
      .Find("[title='Copy full address']")
      .ClickAsync(new MouseEventArgs());
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
    var stop = new PlanStop(
      Guid.NewGuid(),
      "Warehouse",
      "530 Henry St, Rome, NY 13440, US",
      1,
      new(40, -80)
    )
    {
      Job = "Pick Up",
      Commodity = "STEELCOILS",
      Notes =
        "Shipper appointment confirmation number: PU123456. Receiver appointment confirmation number: DL654321. Shipper BOL: 42845601. Service for Load sentinel.",
    };
    var component = context.Render<NextLoadDetailsCard>(p =>
      p.Add(x => x.Route, route).Add(x => x.Stop, stop)
    );
    var reference = component.Find(
      ".fleet-route-popup__location .fleet-route-popup__reference"
    );
    Assert.Equal(
      new[] { "Appt #", "PU123456" },
      reference.Children.Select(x => x.TextContent)
    );
    Assert.True(
      reference.ClassList.Contains("fleet-route-popup__section-start")
    );
    Assert.StartsWith(
      "Pick Up · Load stop 1 of",
      component.Find(".fleet-route-popup__kind").TextContent.Trim()
    );
    Assert.True(
      component
        .Find(".fleet-map-next-load-card__metrics")
        .ClassList.Contains("fleet-route-popup__section-start")
    );
    Assert.DoesNotContain("DL654321", component.Markup);
    Assert.DoesNotContain("42845601", component.Markup);
    Assert.DoesNotContain("STEELCOILS", component.Markup);
    Assert.DoesNotContain("Service for Load", component.Markup);

    component.Render(p => p.Add(x => x.Stop, stop with { Job = "Drop Off" }));
    Assert.Equal(
      new[] { "Appt #", "DL654321" },
      component
        .Find(".fleet-route-popup__reference")
        .Children.Select(x => x.TextContent)
    );
    Assert.DoesNotContain("PU123456", component.Markup);
    component.Render(p =>
      p.Add(
        x => x.Stop,
        stop with
        {
          Job = "Drop Off",
          Notes = "Shipper BOL: 42845601. Service for Load sentinel.",
        }
      )
    );
    Assert.Empty(component.FindAll(".fleet-route-popup__reference"));
    Assert.DoesNotContain("42845601", component.Markup);
    Assert.DoesNotContain("Service for Load", component.Markup);
  }

  [Fact]
  public async Task FutureSelectionSurvivesAnUnchangedPollButClearsWhenTheLoadDisappearsOrNextLoadsAreHidden()
  {
    using var fixture = new SelectionFixture();
    var component = fixture.Render();
    component.WaitForAssertion(
      () => Assert.Contains(fixture.Js.Calls, x => x.Name == "setTrucks")
    );
    await component.InvokeAsync(
      () => component.Instance.OnTruckSelected(fixture.TruckA.ToString())
    );
    var toggle = component.InvokeAsync(
      () =>
        Toggle(component, "Next loads")
          .ChangeAsync(new ChangeEventArgs { Value = true })
    );
    var future = fixture.FutureLoad(202);
    (await fixture.ReadNextAsync()).Reply("future", [future]);
    await toggle;
    var current = fixture.Plan(fixture.TruckA).DispatchId!.Value.ToString();
    await component.InvokeAsync(
      () =>
        component.Instance.OnNextLoadSelected(
          fixture.TruckA.ToString(),
          current,
          future.Id.ToString(),
          0
        )
    );
    await component.InvokeAsync(
      () => fixture.Clock.Advance(TimeSpan.FromSeconds(10))
    );
    (await fixture.ReadNextAsync()).ReplyUnchanged("future");
    component.WaitForAssertion(() =>
    {
      Assert.Single(component.FindAll("[aria-label='Selected next load']"));
      using var payload = JsonSerializer.SerializeToDocument(
        fixture.Js.Calls.Last(x => x.Name == "setStopEtas").Args![0]
      );
      Assert.False(payload.RootElement.GetProperty("Refreshing").GetBoolean());
    });
    await component.InvokeAsync(
      () =>
        Toggle(component, "Next loads")
          .ChangeAsync(new ChangeEventArgs { Value = false })
    );
    Assert.Empty(component.FindAll("[aria-label='Selected next load']"));
    var reopen = component.InvokeAsync(
      () =>
        Toggle(component, "Next loads")
          .ChangeAsync(new ChangeEventArgs { Value = true })
    );
    (await fixture.ReadNextAsync()).ReplyUnchanged("future");
    await reopen;
    await component.InvokeAsync(
      () =>
        component.Instance.OnNextLoadSelected(
          fixture.TruckA.ToString(),
          current,
          future.Id.ToString(),
          0
        )
    );
    await component.InvokeAsync(
      () => fixture.Clock.Advance(TimeSpan.FromSeconds(10))
    );
    (await fixture.ReadNextAsync()).Reply("empty", []);
    component.WaitForAssertion(
      () => Assert.Empty(component.FindAll("[aria-label='Selected next load']"))
    );
    Assert.Single(component.FindAll("[aria-label='Current dispatch route']"));
  }

  private sealed class SelectionFixture : IDisposable
  {
    public Dictionary<Guid, TruckHosSnapshot>? Hos { get; set; }
    public int HosCalls;
    public string AddressA { get; set; } = "";
    public decimal? OutsideA { get; set; }
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
    public int FuelPreviewCalls;
    public int FuelWrites;
    private readonly ClientComponentContext _context;
    private readonly CancellationTokenSource _shutdown = new();
    private readonly Dictionary<Guid, AutomaticPlanningResult> _plans = [];
    private readonly Dictionary<Guid, NextLoadRoute> _futureLoads = [];
    private readonly Channel<PendingNextRequest> _next =
      Channel.CreateUnbounded<PendingNextRequest>();
    private readonly Channel<PendingHttpRequest> _planning =
      Channel.CreateUnbounded<PendingHttpRequest>();
    private readonly Channel<PendingHttpRequest> _details =
      Channel.CreateUnbounded<PendingHttpRequest>();
    private readonly Channel<PendingHttpRequest> _preview =
      Channel.CreateUnbounded<PendingHttpRequest>();

    public SelectionFixture(bool cacheCurrentPlans = true)
    {
      foreach (var truck in new[] { TruckA, TruckB })
      {
        var dispatch = Guid.NewGuid();
        _plans[truck] = new(
          truck,
          dispatch,
          1358,
          new(
            new(),
            new()
            {
              Id = Guid.NewGuid(),
              DispatchId = dispatch,
              TruckId = truck,
              Version = 1,
              Route = new()
              {
                Legs = [new(10, 600, [new(40, -80), new(41, -79)])],
              },
            },
            null,
            null,
            null,
            true
          ),
          null
        );
      }
      _context = new ClientComponentContext(RespondAsync);
      _context.Services.AddSingleton<TimeProvider>(Clock);
      _context.Services.AddSingleton<IJSRuntime>(Js);
      _context.Services.AddSingleton<IConfiguration>(
        new ConfigurationBuilder().Build()
      );
      var cache = _context.Services.GetRequiredService<PlanningDisplayCache>();
      if (cacheCurrentPlans)
        foreach (var (truck, plan) in _plans)
          cache.Store($"api/fleet/trucks/{truck}/planning", plan);
    }

    public IRenderedComponent<FleetMap> Render(Guid? dispatchId = null)
    {
      if (dispatchId is { } id)
        _context
          .Services.GetRequiredService<NavigationManager>()
          .NavigateTo($"/fleet/map?dispatchId={id}");
      return _context.Render<FleetMap>();
    }

    public IRenderedComponent<CascadingValue<DisplayUnits>> RenderWithUnits() =>
      _context.Render<CascadingValue<DisplayUnits>>(parameters =>
        parameters
          .Add(cascade => cascade.Value, DisplayUnits.Default)
          .AddChildContent<FleetMap>()
      );

    public Task<PendingNextRequest> ReadNextAsync() =>
      _next.Reader.ReadAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));

    public Task<PendingHttpRequest> ReadPlanningAsync() =>
      _planning.Reader.ReadAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));

    public Task<PendingHttpRequest> ReadDetailsAsync() =>
      _details.Reader.ReadAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));

    public Task<PendingHttpRequest> ReadPreviewAsync() =>
      _preview.Reader.ReadAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));

    public AutomaticPlanningResult Plan(Guid truck) => _plans[truck];

    public void SetEta(Guid truck, DispatchEta? eta)
    {
      SetPlanningResult(
        truck,
        _plans[truck] with
        {
          State = _plans[truck].State! with { Eta = eta },
        }
      );
    }

    public void SetPlanningResult(Guid truck, AutomaticPlanningResult result)
    {
      _plans[truck] = result;
      _context
        .Services.GetRequiredService<PlanningDisplayCache>()
        .Store($"api/fleet/trucks/{truck}/planning", _plans[truck]);
    }

    public AutomaticPlanningResult? CachedPlan(Guid truck) =>
      _context
        .Services.GetRequiredService<PlanningDisplayCache>()
        .Get($"api/fleet/trucks/{truck}/planning");

    public AutomaticPlanningResult? CachedDispatchPlan(Guid dispatch) =>
      _context
        .Services.GetRequiredService<PlanningDisplayCache>()
        .Get($"api/dispatch/{dispatch}/planning/automatic");

    public void StoreDispatchAlias(AutomaticPlanningResult result) =>
      _context
        .Services.GetRequiredService<PlanningDisplayCache>()
        .Store($"api/dispatch/{result.DispatchId}/planning/automatic", result);

    public AutomaticPlanningResult WithoutGeometry(
      AutomaticPlanningResult saved
    )
    {
      var plan = saved.State!.Plan!;
      return saved with
      {
        State = saved.State with
        {
          Plan = new()
          {
            Id = plan.Id,
            TruckId = plan.TruckId,
            DispatchId = plan.DispatchId,
            Version = plan.Version,
            GeometryOmitted = true,
            Route = new()
            {
              Legs = plan
                .Route.Legs.Select(x => x with { Points = [] })
                .ToList(),
            },
          },
        },
      };
    }

    public JsonDocument LastNextPayload() =>
      JsonDocument.Parse(
        (byte[])Js.Calls.Last(x => x.Name == "setNextLoadsBytes").Args![0]!
      );

    public JsonDocument LastCurrentPayload() =>
      JsonDocument.Parse(
        (byte[])Js.Calls.Last(x => x.Name == "setRouteBytes").Args![0]!
      );

    public JsonElement LastLoadReference() =>
      JsonSerializer.SerializeToElement(
        Js.Calls.Last(x => x.Name == "setLoadReference").Args![0],
        new JsonSerializerOptions(JsonSerializerDefaults.Web)
      );

    public void AssertLoadReference(
      Guid dispatchId,
      int loadNumber,
      string orderNumber
    )
    {
      var reference = LastLoadReference();
      Assert.Equal(4, reference.EnumerateObject().Count());
      Assert.Equal(dispatchId, reference.GetProperty("dispatchId").GetGuid());
      Assert.Equal(loadNumber, reference.GetProperty("loadNumber").GetInt32());
      Assert.Equal(
        loadNumber.ToString(CultureInfo.InvariantCulture),
        reference.GetProperty("loadLabel").GetString()
      );
      Assert.Equal(
        orderNumber,
        reference.GetProperty("orderNumber").GetString()
      );
    }

    public NextLoadRoute FutureLoad(int number)
    {
      var route = new NextLoadRoute(
        Guid.NewGuid(),
        number,
        "ready",
        [new(10, 600, [new(40, -80), new(41, -79)])],
        [
          new(40, -80, "Pickup") { Id = Guid.NewGuid() },
          new(41, -79, "Delivery") { Id = Guid.NewGuid() },
        ]
      );
      _futureLoads[route.Id] = route;
      return route;
    }

    public NextLoadRoute CurrentLoad(Guid truck) =>
      FutureLoad(1358) with
      {
        Id = _plans[truck].DispatchId!.Value,
      };

    public void AssertIdentity(
      PendingNextRequest request,
      Guid truck,
      string revision
    )
    {
      Assert.Equal(
        $"/api/dispatch/truck/{truck}/next-routes",
        request.Uri.AbsolutePath
      );
      Assert.Equal(
        $"?currentDispatchId={_plans[truck].DispatchId}&revision={revision}",
        request.Uri.Query
      );
    }

    public void AssertOnlyFuturePublished(Guid id)
    {
      var call = Assert.Single(Js.Calls, x => x.Name == "setNextLoadsBytes");
      using var json = JsonDocument.Parse((byte[])call.Args![0]!);
      var route = Assert.Single(
        json.RootElement.GetProperty("routes").EnumerateArray()
      );
      Assert.Equal(id, route.GetProperty("id").GetGuid());
    }

    private Task<HttpResponseMessage> RespondAsync(
      HttpRequestMessage request,
      CancellationToken ct
    )
    {
      Interlocked.Increment(ref HttpCalls);
      var uri = request.RequestUri!;
      if (uri.AbsolutePath.EndsWith("/weather", StringComparison.Ordinal))
        return Task.FromResult(
          Ok(
            uri.AbsolutePath.Contains(TruckA.ToString())
            && OutsideA is { } value
              ? new Client.Models.DTO.Fleet.WeatherReadingDto(
                value,
                "CLEAR",
                "Clear",
                true,
                Clock.GetUtcNow()
              )
              : null
          )
        );
      if (
        uri.AbsolutePath.EndsWith("/camera", StringComparison.Ordinal)
        || uri.AbsolutePath.EndsWith(
          "/planning/route/options",
          StringComparison.Ordinal
        )
      )
        return Task.FromResult(
          new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)
        );
      if (uri.AbsolutePath == "/api/fleet/hos")
      {
        Interlocked.Increment(ref HosCalls);
        return Task.FromResult(
          Ok(Hos is null ? (object)Array.Empty<object>() : Hos)
        );
      }
      if (
        uri.AbsolutePath is "/api/fuel/stations" or "/api/fuel/price-overview"
      )
        return Task.FromResult(Ok(Array.Empty<object>()));
      if (
        uri.AbsolutePath.EndsWith(
          "/planning/fuel/recalculate",
          StringComparison.Ordinal
        )
        || uri.AbsolutePath.EndsWith(
          "/planning/fuel/reset",
          StringComparison.Ordinal
        )
      )
      {
        Interlocked.Increment(ref FuelWrites);
        return Task.FromResult(
          new HttpResponseMessage(HttpStatusCode.OK)
          {
            Content = JsonContent.Create(
              new
              {
                success = false,
                errors = new[] { "Fuel calculation unavailable." },
              }
            ),
          }
        );
      }
      if (
        _plans.Values.FirstOrDefault(plan =>
          uri.AbsolutePath
          == $"/api/dispatch/{plan.DispatchId}/planning/fuel/edit/preview"
        ) is
        { } editing
      )
      {
        Interlocked.Increment(ref FuelPreviewCalls);
        var fuel =
          editing.State!.Plan!.FuelPlan
          ?? new FuelPlan { TruckId = editing.TruckId };
        return Task.FromResult(
          Ok(
            new FuelPlanEditPreview(
              fuel,
              fuel.Stops.Select(stop => new FuelPlanEditStop(
                  stop.StationId,
                  stop.BeforeStopId,
                  stop.BuyGallons,
                  stop.FillToTarget
                ))
                .ToList(),
              editing.State.Plan.FuelPlan?.CalculatedAt,
              250,
              250,
              []
            )
          )
        );
      }
      if (uri.AbsolutePath.EndsWith("/next-routes", StringComparison.Ordinal))
      {
        Interlocked.Increment(ref NextCalls);
        var pending = new PendingNextRequest(uri, ct);
        _next.Writer.TryWrite(pending);
        return pending.Response.Task.WaitAsync(_shutdown.Token);
      }
      if (uri.AbsolutePath == "/api/fleet/locations")
        return Task.FromResult(
          Ok(
            new
            {
              trucks = new[]
              {
                new
                {
                  truckId = TruckA,
                  unitNumber = "54777",
                  formattedLocation = AddressA,
                  updatedAt = Clock.GetUtcNow().UtcDateTime,
                  outsideTemperatureCelsius = OutsideA,
                  outsideTemperatureUpdatedAt = (DateTime?)
                    Clock.GetUtcNow().UtcDateTime,
                },
                new
                {
                  truckId = TruckB,
                  unitNumber = "64888",
                  formattedLocation = "",
                  updatedAt = default(DateTime),
                  outsideTemperatureCelsius = (decimal?)null,
                  outsideTemperatureUpdatedAt = (DateTime?)null,
                },
              },
              points = Array.Empty<object>(),
            }
          )
        );
      if (
        uri.AbsolutePath.EndsWith("/planning/preview", StringComparison.Ordinal)
      )
      {
        Interlocked.Increment(ref PreviewCalls);
        if (PreviewUnavailable)
          return Task.FromResult(
            new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)
          );
        return DeferPreview
          ? Defer(_preview, uri, ct)
          : Task.FromResult(Ok(_plans[Guid.Parse(uri.Segments[^3].Trim('/'))]));
      }
      // Before the dispatch "/planning" suffix: the fleet map asks this one
      // for the price basis it colours stations by.
      if (uri.AbsolutePath == "/api/settings/planning")
        return Task.FromResult(
          Ok(new { preferences = new { useIfta = true }, revision = 1 })
        );
      if (uri.AbsolutePath.EndsWith("/planning", StringComparison.Ordinal))
      {
        Interlocked.Increment(ref PlanningCalls);
        return DeferPlanning
          ? Defer(_planning, uri, ct)
          : Task.FromResult(Ok(_plans[Guid.Parse(uri.Segments[^2].Trim('/'))]));
      }
      if (
        _plans.Values.FirstOrDefault(x =>
          uri.AbsolutePath == $"/api/dispatch/{x.DispatchId}/planning/automatic"
        ) is
        { } automatic
      )
      {
        Interlocked.Increment(ref PlanningCalls);
        return DeferPlanning
          ? Defer(_planning, uri, ct)
          : Task.FromResult(Ok(automatic));
      }
      if (
        _plans.Values.FirstOrDefault(x =>
          uri.AbsolutePath == $"/api/dispatch/{x.DispatchId}"
        ) is
        { } plan
      )
      {
        Interlocked.Increment(ref DetailsCalls);
        return DeferDetails
          ? Defer(_details, uri, ct)
          : Task.FromResult(
            Ok(new { id = plan.DispatchId, loadNumber = plan.LoadNumber })
          );
      }
      if (
        _futureLoads.Values.FirstOrDefault(x =>
          uri.AbsolutePath == $"/api/dispatch/{x.Id}"
        ) is
        { } future
      )
      {
        Interlocked.Increment(ref DetailsCalls);
        return DeferDetails
          ? Defer(_details, uri, ct)
          : Task.FromResult(
            Ok(new { id = future.Id, loadNumber = future.LoadNumber })
          );
      }
      if (
        uri.AbsolutePath.StartsWith(
          "/api/dispatch/truck/",
          StringComparison.Ordinal
        )
      )
        Interlocked.Increment(ref DetailsCalls);
      return Task.FromResult(Ok(Array.Empty<object>()));
    }

    private Task<HttpResponseMessage> Defer(
      Channel<PendingHttpRequest> channel,
      Uri uri,
      CancellationToken ct
    )
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

    // A completed HTTP response can race cancellation; the component must still
    // reject its old identity/version.
    public TaskCompletionSource<HttpResponseMessage> Response { get; } =
      new(TaskCreationOptions.RunContinuationsAsynchronously);

    public void Reply(object response) => Response.SetResult(Ok(response));
  }

  private sealed class PendingNextRequest(
    Uri uri,
    CancellationToken cancellation
  ) : PendingHttpRequest(uri, cancellation)
  {
    public void Reply(string revision, IReadOnlyList<NextLoadRoute> routes) =>
      Response.SetResult(
        Ok(new NextLoadRoutesResponse(revision, false, routes))
      );

    public void ReplyUnchanged(string revision) =>
      Response.SetResult(Ok(new NextLoadRoutesResponse(revision, true, null)));

    public void ReplyLabels(
      string revision,
      IReadOnlyList<NextLoadLabels> labels
    ) =>
      Response.SetResult(
        Ok(new NextLoadRoutesResponse(revision, false, null, labels))
      );
  }

  private static HttpResponseMessage Ok(object? response) =>
    new(HttpStatusCode.OK)
    {
      Content = JsonContent.Create(new { success = true, response }),
    };

  private sealed class Fixture : IDisposable
  {
    public Guid TruckId { get; } = Guid.NewGuid();
    private readonly Guid _dispatchId = Guid.NewGuid();
    public FakeTimeProvider Clock { get; } = new();
    public MapJs Js { get; } = new();
    public int NextCalls;
    public int StationCalls;
    public bool FailLocations;
    public bool FleetUsesIfta = true;
    private readonly ClientComponentContext _context;

    public Fixture()
    {
      _context = new ClientComponentContext(
        (request, _) => Task.FromResult(Respond(request.RequestUri!))
      );
      _context.Services.AddSingleton<TimeProvider>(Clock);
      _context.Services.AddSingleton<IJSRuntime>(Js);
      _context.Services.AddSingleton<IConfiguration>(
        new ConfigurationBuilder().Build()
      );
    }

    public IRenderedComponent<FleetMap> Render(Guid? truckId = null)
    {
      if (truckId is { } target)
        _context
          .Services.GetRequiredService<NavigationManager>()
          .NavigateTo($"/fleet/map?truckId={target}");
      return _context.Render<FleetMap>();
    }

    private HttpResponseMessage Respond(Uri uri)
    {
      if (uri.AbsolutePath == "/api/fuel/stations")
      {
        StationCalls++;
        return Ok(Array.Empty<object>());
      }
      if (uri.AbsolutePath == "/api/fuel/price-overview")
        return Ok(Array.Empty<object>());
      if (uri.AbsolutePath == "/api/fleet/locations")
        return FailLocations
          ? new(HttpStatusCode.ServiceUnavailable)
          : Ok(
            new
            {
              trucks = new[]
              {
                new { truckId = TruckId, unitNumber = "54777" },
              },
              points = Array.Empty<object>(),
            }
          );
      if (uri.AbsolutePath == "/api/settings/planning")
        return Ok(
          new { preferences = new { useIfta = FleetUsesIfta }, revision = 1 }
        );
      if (uri.AbsolutePath.EndsWith("/planning"))
        return Ok(
          new
          {
            truckId = TruckId,
            dispatchId = _dispatchId,
            loadNumber = 1358,
          }
        );
      if (uri.AbsolutePath.EndsWith("/next-routes"))
      {
        NextCalls++;
        return NextCalls == 2
          ? new(HttpStatusCode.ServiceUnavailable)
          : Ok(
            new
            {
              revision = "saved",
              unchanged = NextCalls > 2,
              routes = NextCalls == 1 ? Array.Empty<object>() : null,
            }
          );
      }
      if (uri.AbsolutePath == $"/api/dispatch/{_dispatchId}")
        return Ok(new { id = _dispatchId, loadNumber = 1358 });
      return Ok(Array.Empty<object>());
    }

    public void Dispose() => _context.Dispose();
  }

  private sealed class MapJs : IJSRuntime, IJSObjectReference
  {
    public ConcurrentQueue<(string Name, object?[]? Args)> Calls { get; } =
      new();
    private readonly Channel<byte[]> _currentPayloads =
      Channel.CreateUnbounded<byte[]>();
    private readonly Channel<byte[]> _nextPayloads =
      Channel.CreateUnbounded<byte[]>();

    public Task<byte[]> ReadCurrentPayloadAsync() =>
      _currentPayloads
        .Reader.ReadAsync()
        .AsTask()
        .WaitAsync(TimeSpan.FromSeconds(5));

    public Task<byte[]> ReadNextPayloadAsync() =>
      _nextPayloads
        .Reader.ReadAsync()
        .AsTask()
        .WaitAsync(TimeSpan.FromSeconds(5));

    public ValueTask<TValue> InvokeAsync<TValue>(
      string identifier,
      object?[]? args
    ) => InvokeAsync<TValue>(identifier, default, args);

    public ValueTask<TValue> InvokeAsync<TValue>(
      string identifier,
      CancellationToken ct,
      object?[]? args
    )
    {
      ct.ThrowIfCancellationRequested();
      Calls.Enqueue((identifier, args));
      if (identifier == "setRouteBytes")
        _currentPayloads.Writer.TryWrite((byte[])args![0]!);
      if (identifier == "setNextLoadsBytes")
        _nextPayloads.Writer.TryWrite((byte[])args![0]!);
      object? result =
        typeof(TValue) == typeof(IJSObjectReference) ? this
        : typeof(TValue) == typeof(bool) ? true
        : default(TValue);
      return ValueTask.FromResult((TValue)result!);
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
  }
}
