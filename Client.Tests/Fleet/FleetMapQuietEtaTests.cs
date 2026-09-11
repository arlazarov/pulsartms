using System.Text.Json;
using Bunit;
using Client.Models.DTO.Planning;
using Client.Pages.FleetMap;
using Client.Tests.Support;

namespace Client.Tests.Fleet;

[Trait("Category", "Fleet")]
[Trait("Kind", "Component")]
public sealed class FleetMapQuietEtaTests
{
  [Fact]
  public async Task VisibleSavedRouteHidesOnlyMaintenanceNoiseAndKeepsAddressWarnings()
  {
    using var fixture = new QuietEtaMapFixture();
    fixture.Planning = fixture.Planning with { Message = "Route service is temporarily unavailable. Retrying automatically." };
    var component = await fixture.SelectAsync();
    Assert.DoesNotContain("Retrying automatically", component.Markup);
    Assert.Contains("02:00 PM", FutureEta(component));

    fixture.Planning = fixture.Planning with { Message = "Delivery address needs verified coordinates." };
    await component.InvokeAsync(() => fixture.Clock.Advance(TimeSpan.FromSeconds(10)));
    component.WaitForAssertion(() => Assert.Contains("Delivery address needs verified coordinates.", component.Markup));
  }

  [Fact]
  public async Task OlderForecastWithoutPendingDispatchMetadataCanRenderTheSelectedFutureStop()
  {
    using var fixture = new QuietEtaMapFixture();
    fixture.Planning = fixture.Planning with { State = fixture.Planning.State! with
      { Eta = fixture.Planning.State!.Eta! with { PendingDispatches = null! } } };
    var component = await fixture.SelectAsync();
    Assert.Contains("02:00 PM", FutureEta(component));
  }

  [Fact]
  public async Task InFlightPlanningAcrossValidityDeadlineRetainsCurrentAndFutureEtaUntilReplacement()
  {
    using var fixture = new QuietEtaMapFixture();
    var component = await fixture.SelectAsync();
    var original = fixture.Planning;
    var forecast = original.State!.Eta!;
    var currentHtml = CurrentEta(component);
    var futureHtml = FutureEta(component);
    fixture.HoldPlanning = true;
    await component.InvokeAsync(() => fixture.Clock.Advance(TimeSpan.FromSeconds(10)));
    var pending = await fixture.ReadPendingAsync();
    await component.InvokeAsync(() => fixture.Clock.Advance(TimeSpan.FromSeconds(111)));
    component.Render();
    Assert.False(pending.Task.IsCompleted);
    component.WaitForAssertion(() =>
    {
      Assert.Equal(currentHtml, CurrentEta(component));
      Assert.Equal(futureHtml, FutureEta(component));
      Assert.Equal(115, component.FindComponent<NextLoadDetailsCard>().Instance.DistanceMiles);
      Assert.DoesNotContain("Updating", component.Markup);
      Assert.DoesNotContain("Previous", component.Markup);
    });
    Assert.Equal(forecast.Stops, PublishedEta(fixture).Stops);
    Assert.Equal(forecast.ValidUntil, PublishedEta(fixture).ValidUntil);
    QuietEtaMapFixture.Reply(pending, original with { State = original.State with { Eta = null } });
    component.WaitForAssertion(() =>
    {
      Assert.False(PublishedState(fixture).GetProperty("Refreshing").GetBoolean());
      Assert.True(PublishedEta(fixture).RouteUpdatePending);
    });
    Assert.Equal(currentHtml, CurrentEta(component));
    Assert.Equal(futureHtml, FutureEta(component));
    Assert.Equal(forecast.Stops, PublishedEta(fixture).Stops);
    Assert.Equal(forecast.ValidUntil, PublishedEta(fixture).ValidUntil);
    var now = fixture.Clock.GetUtcNow().UtcDateTime;
    var updated = forecast with { CalculatedAt = now, ValidUntil = now.AddMinutes(2),
      Stops = forecast.Stops.Select(stop => stop with { Arrival = stop.Arrival.AddMinutes(5) }).ToArray() };
    await component.InvokeAsync(() => fixture.Clock.Advance(TimeSpan.FromSeconds(10)));
    QuietEtaMapFixture.Reply(await fixture.ReadPendingAsync(), original with { State = original.State with { Eta = updated } });
    component.WaitForAssertion(() =>
    {
      Assert.NotEqual(currentHtml, CurrentEta(component));
      Assert.NotEqual(futureHtml, FutureEta(component));
      Assert.Equal(updated.Stops[1].Arrival, component.FindComponent<NextLoadDetailsCard>().Instance.Eta!.Stops[0].Arrival);
    });
  }

  [Fact]
  public async Task VersionOnlyRerouteRetainsEtaAndDistanceButExplicitUnavailableReplacementClearsThem()
  {
    using var fixture = new QuietEtaMapFixture();
    var component = await fixture.SelectAsync();
    var currentHtml = CurrentEta(component);
    var futureHtml = FutureEta(component);
    var replacement = JsonSerializer.Deserialize<AutomaticPlanningResult>(JsonSerializer.Serialize(fixture.Planning))!;
    replacement.State!.Plan!.Version++;
    replacement = replacement with { State = replacement.State with { Eta = null,
      Progress = replacement.State.Progress! with { ProgressMiles = 20, RemainingMiles = 80 } } };
    fixture.Planning = replacement;
    await component.InvokeAsync(() => fixture.Clock.Advance(TimeSpan.FromSeconds(10)));
    component.WaitForAssertion(() => Assert.Equal(replacement.State.Plan.Version,
      JsonSerializer.SerializeToElement(fixture.Js.Calls.Last(call => call.Name == "setStopEtas").Args![0])
        .GetProperty("PlanVersion").GetInt32()));
    Assert.Equal(currentHtml, CurrentEta(component));
    Assert.Equal(futureHtml, FutureEta(component));
    Assert.Equal(115, component.FindComponent<NextLoadDetailsCard>().Instance.DistanceMiles);
    using var payload = JsonDocument.Parse((byte[])fixture.Js.Calls.Last(call => call.Name == "setRouteBytes").Args![0]!);
    Assert.Equal(replacement.State.Plan.Version, payload.RootElement.GetProperty("version").GetInt32());
    var geometryProgress = Assert.IsType<RouteProgress>(fixture.Js.Calls.Last(call => call.Name == "setRouteBytes").Args![1]);
    Assert.Equal(20, geometryProgress.ProgressMiles);
    Assert.Equal(80, geometryProgress.RemainingMiles);
    var now = fixture.Clock.GetUtcNow().UtcDateTime;
    fixture.Planning = replacement with { State = replacement.State with
      { Eta = new(now, now.AddMinutes(2), [], "ETA unavailable: this regional ruleset needs verification.", []) } };
    await component.InvokeAsync(() => fixture.Clock.Advance(TimeSpan.FromSeconds(10)));
    component.WaitForAssertion(() =>
    {
      Assert.DoesNotContain("01:00 PM", CurrentEta(component));
      Assert.Empty(component.FindAll("[aria-label='Selected next load'] .arrival-estimate"));
      Assert.DoesNotContain("ETA unavailable", component.Markup);
      Assert.Equal(105, component.FindComponent<NextLoadDetailsCard>().Instance.DistanceMiles);
    });
  }

  [Fact]
  public async Task PartialCurrentForecastRetainsOnlyThePendingFutureWithinItsOriginalGrace()
  {
    using var fixture = new QuietEtaMapFixture();
    var component = await fixture.SelectAsync();
    var original = fixture.Planning;
    var previous = original.State!.Eta!;
    var currentHtml = CurrentEta(component);
    var futureHtml = FutureEta(component);
    var partial = previous with { CalculatedAt = previous.CalculatedAt.AddSeconds(10),
      ValidUntil = previous.ValidUntil.AddSeconds(10), Stops = [previous.Stops[0] with { Arrival = previous.Stops[0].Arrival.AddMinutes(5) }],
      PendingDispatches = new Dictionary<Guid, string> { [fixture.Future.Id] = "Waiting for the saved connection." } };
    Assert.False(partial.RouteUpdatePending);
    fixture.Planning = original with { State = original.State with { Eta = partial } };
    await component.InvokeAsync(() => fixture.Clock.Advance(TimeSpan.FromSeconds(10)));
    component.WaitForAssertion(() => Assert.Equal(partial.CalculatedAt, PublishedEta(fixture).CalculatedAt));
    Assert.NotEqual(currentHtml, CurrentEta(component));
    Assert.Equal(futureHtml, FutureEta(component));
    Assert.Contains(fixture.Future.Id, PublishedEta(fixture).PendingDispatches.Keys);
    Assert.DoesNotContain("Updating", component.Markup);
    fixture.HoldPlanning = true;
    await component.InvokeAsync(() => fixture.Clock.Advance(TimeSpan.FromMinutes(17)));
    var waiting = await fixture.ReadPendingAsync();
    component.Render();
    component.WaitForAssertion(() => Assert.DoesNotContain("02:00 PM",
      component.Find("[aria-label='Selected next load']").TextContent));
    var now = fixture.Clock.GetUtcNow().UtcDateTime;
    var fresh = previous with { CalculatedAt = now, ValidUntil = now.AddMinutes(2),
      Stops = previous.Stops.Select(stop => stop with { Arrival = stop.Arrival.AddMinutes(10) }).ToArray() };
    QuietEtaMapFixture.Reply(waiting, original with { State = original.State with { Eta = fresh } });
    component.WaitForAssertion(() => Assert.Equal(fresh.Stops[1].Arrival,
      component.FindComponent<NextLoadDetailsCard>().Instance.Eta!.Stops[0].Arrival));
    Assert.DoesNotContain("Updating", component.Markup);
  }

  private static string CurrentEta(IRenderedComponent<FleetMap> component) =>
    component.Find("[aria-label='Current dispatch route'] .arrival-estimate").OuterHtml;
  private static string FutureEta(IRenderedComponent<FleetMap> component) =>
    component.Find("[aria-label='Selected next load'] .arrival-estimate").OuterHtml;
  private static DispatchEta PublishedEta(QuietEtaMapFixture fixture) =>
    PublishedState(fixture).GetProperty("Eta").Deserialize<DispatchEta>()!;
  private static JsonElement PublishedState(QuietEtaMapFixture fixture) =>
    JsonSerializer.SerializeToElement(fixture.Js.Calls.Last(call => call.Name == "setStopEtas").Args![0]);
}
