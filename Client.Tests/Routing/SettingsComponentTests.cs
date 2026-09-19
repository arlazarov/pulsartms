using System.Net;
using System.Net.Http.Json;
using Bunit;
using Client.Models.DTO;
using Client.Models.DTO.Planning;
using Client.Tests.Support;
using SettingsPage = Client.Pages.Settings.Settings;

namespace Client.Tests.Routing;

[Trait("Category", "Routing")]
[Trait("Kind", "Component")]
public sealed class SettingsComponentTests
{
  [Fact]
  public async Task ConflictPreservesDraftAndReloadUsesTheNewRevision()
  {
    var loads = 0;
    var saves = new List<PlanningSettingsUpdate>();
    using var context = new ClientComponentContext(async (request, ct) =>
    {
      if (request.RequestUri!.AbsolutePath == "/api/settings/dispatch") return NumberingResponse();
      if (request.RequestUri.AbsolutePath == "/api/settings/integrations") return IntegrationSettingsFixture.ListResponse();
      if (request.Method == HttpMethod.Get)
      {
        loads++;
        return Response(new(new() { UseIfta = true }, loads == 1 ? 3 : 4, null));
      }
      saves.Add((await request.Content!.ReadFromJsonAsync<PlanningSettingsUpdate>(ct))!);
      return saves.Count == 1
        ? new(HttpStatusCode.Conflict) { Content = JsonContent.Create(new RequestResponseDTO<PlanningSettingsState>
          { Success = false, Errors = ["Settings changed. Reload settings and try again."] }) }
        : Response(new(saves[^1].Preferences, 5, null));
    });
    var component = context.Render<SettingsPage>();
    component.WaitForElement("#settings-ifta").Change(false);
    await component.Find(".planning-settings__form").SubmitAsync(EventArgs.Empty);
    Assert.Equal(3, Assert.Single(saves).Revision);
    Assert.False(saves[0].Preferences.UseIfta);
    Assert.False(component.Find("#settings-ifta").HasAttribute("checked"));
    Assert.Contains("Settings changed", component.Find("[role=alert]").TextContent);
    await component.Find("[role=alert] button").ClickAsync(new());
    Assert.True(component.Find("#settings-ifta").HasAttribute("checked"));
    await component.Find(".planning-settings__form").SubmitAsync(EventArgs.Empty);
    Assert.Equal(4, saves[^1].Revision);
    Assert.Contains("Settings saved", component.Find(".settings-page__saved").TextContent);
  }

  [Fact]
  public void FuelStopControlsAndBroadRestoreActionAreAbsentWithoutAnAutomaticWrite()
  {
    var writes = 0;
    using var context = new ClientComponentContext((request, _) =>
    {
      if (request.RequestUri!.AbsolutePath == "/api/settings/dispatch") return Task.FromResult(NumberingResponse());
      if (request.RequestUri.AbsolutePath == "/api/settings/integrations") return Task.FromResult(IntegrationSettingsFixture.ListResponse());
      if (request.Method != HttpMethod.Get) writes++;
      return Task.FromResult(Response(new(new(), 1, null)));
    });
    var component = context.Render<SettingsPage>();
    component.WaitForElement("#settings-ifta");
    Assert.Equal(0, writes);
    Assert.Empty(component.FindAll("#settings-driving-cost, #settings-reserve, #settings-fill, #settings-detour"));
    Assert.DoesNotContain("Fuel stops", component.Markup);
    Assert.DoesNotContain("Restore defaults", component.Markup);
    Assert.Contains("Fuel prices", component.Markup);
    Assert.Contains("Load numbers", component.Markup);
    Assert.Empty(component.FindAll(".settings-page__saved"));
  }

  [Theory]
  [InlineData(false)]
  [InlineData(true)]
  public async Task SavingPriceBasisPreservesTheServerProvidedHiddenPreferences(bool useIfta)
  {
    PlanningSettingsUpdate? saved = null;
    using var context = new ClientComponentContext(async (request, ct) =>
    {
      if (request.RequestUri!.AbsolutePath == "/api/settings/dispatch") return NumberingResponse();
      if (request.RequestUri.AbsolutePath == "/api/settings/integrations") return IntegrationSettingsFixture.ListResponse();
      if (request.Method == HttpMethod.Get)
        return Response(new(new() { UseIfta = !useIfta, MaxDetourMinutes = 47,
          DriverHourlyCostUsd = 35, ReserveGallons = 25, FillPercent = 100, StopCostUsd = 27, CadToUsd = .73 }, 3, null));
      saved = await request.Content!.ReadFromJsonAsync<PlanningSettingsUpdate>(ct);
      return Response(new(saved!.Preferences, 4, null));
    });
    var component = context.Render<SettingsPage>();
    component.WaitForElement("#settings-ifta").Change(useIfta);
    await component.Find(".planning-settings__form").SubmitAsync(EventArgs.Empty);
    Assert.NotNull(saved);
    Assert.Equal(useIfta, saved.Preferences.UseIfta);
    Assert.Equal(35, saved.Preferences.DriverHourlyCostUsd);
    Assert.Equal(25, saved.Preferences.ReserveGallons);
    Assert.Equal(100, saved.Preferences.FillPercent);
    Assert.Equal(27, saved.Preferences.StopCostUsd);
    Assert.Equal(.73, saved.Preferences.CadToUsd);
    Assert.Equal(47, saved.Preferences.MaxDetourMinutes);
    Assert.Equal(3, saved.Revision);
  }

  [Theory]
  [InlineData(-1)]
  [InlineData(61)]
  public async Task InvalidServerProvidedPreferencesStillDoNotReachTheServer(double detour)
  {
    var writes = 0;
    using var context = new ClientComponentContext((request, _) =>
    {
      if (request.RequestUri!.AbsolutePath == "/api/settings/dispatch") return Task.FromResult(NumberingResponse());
      if (request.RequestUri.AbsolutePath == "/api/settings/integrations") return Task.FromResult(IntegrationSettingsFixture.ListResponse());
      if (request.Method != HttpMethod.Get) writes++;
      return Task.FromResult(Response(new(new() { MaxDetourMinutes = detour }, 1, null)));
    });
    var component = context.Render<SettingsPage>();
    component.WaitForElement("#settings-ifta").Change(false);
    await component.Find(".planning-settings__form").SubmitAsync(EventArgs.Empty);
    Assert.Equal(0, writes);
    Assert.Contains("Maximum detour must be between 1 and 60 minutes.", component.Markup);
    Assert.Empty(component.FindAll(".settings-page__saved"));
  }

  [Fact]
  public async Task SavingFuelPreferencesDoesNotSubmitOrResetAnIntegrationDraft()
  {
    var writes = new List<string>();
    using var context = new ClientComponentContext(async (request, ct) =>
    {
      if (request.RequestUri!.AbsolutePath == "/api/settings/dispatch") return NumberingResponse();
      if (request.RequestUri.AbsolutePath == "/api/settings/integrations") return IntegrationSettingsFixture.ListResponse();
      if (request.Method == HttpMethod.Get) return Response(new(new(), 3, null));
      writes.Add(request.RequestUri.AbsolutePath);
      var saved = (await request.Content!.ReadFromJsonAsync<PlanningSettingsUpdate>(ct))!;
      return Response(new(saved.Preferences, 4, null));
    });
    var component = context.Render<SettingsPage>();
    await component.WaitForElement("[data-provider='samsara'] button").ClickAsync(new());
    component.Find("#integration-samsara-apiKey").Input("independent-secret-draft");
    // Integration and planning settings load independently; the form may render after the provider list.
    component.WaitForElement("#settings-ifta").Change(false);
    await component.Find(".planning-settings__form").SubmitAsync(EventArgs.Empty);
    Assert.Equal("/api/settings/planning", Assert.Single(writes));
    Assert.Equal("independent-secret-draft", component.Find("#integration-samsara-apiKey").GetAttribute("value"));
    Assert.Empty(component.FindAll(".integration-settings .settings-page__saved"));
  }

  private static HttpResponseMessage Response(PlanningSettingsState state) => new(HttpStatusCode.OK)
    { Content = JsonContent.Create(new RequestResponseDTO<PlanningSettingsState> { Success = true, Response = state }) };

  private static HttpResponseMessage NumberingResponse() => new(HttpStatusCode.OK)
    { Content = JsonContent.Create(new RequestResponseDTO<DispatchSettingsState> { Success = true, Response = new("AMF", 0, null) }) };
}
