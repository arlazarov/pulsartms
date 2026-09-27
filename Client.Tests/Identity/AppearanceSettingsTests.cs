using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using AngleSharp.Html.Dom;
using Bunit;
using Client.Models.DTO;
using Client.Models.DTO.Identity;
using Client.Shared.Appearance.AppearanceProvider;
using Client.Shared.Measurements.DistanceText;
using Client.Tests.Support;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using FleetSettings = Client.Pages.Settings.FleetSettings;
using SettingsPage = Client.Pages.Settings.PersonalSettings;
using ThemeControl = Client.Pages.Settings.AppearanceSettings;

namespace Client.Tests.Identity;

[Trait("Category", "Identity")]
[Trait("Kind", "Component")]
public sealed class AppearanceSettingsTests
{
  [Fact]
  public async Task InitialReadShowsAnimationUntilPreferencesAreApplied()
  {
    var pending = new TaskCompletionSource<HttpResponseMessage>();
    await using var context = new ClientComponentContext(
      (_, _) => pending.Task
    );
    context
      .JSInterop.SetupModule("./js/generated/shared/appearance.js")
      .SetupVoid("applyTheme", _ => true)
      .SetVoidResult();
    var component = Render(context, Account("one"));
    var loader = component.WaitForElement(".appearance-loader[role=status]");
    Assert.Equal(
      "Loading…",
      loader.QuerySelector(".visually-hidden")!.TextContent
    );
    Assert.Equal(
      3,
      loader.QuerySelectorAll("[aria-hidden=true] > span").Length
    );
    Assert.Empty(component.FindAll("button"));
    Assert.DoesNotContain("Loading personal preferences", component.Markup);

    pending.SetResult(Response("dark"));
    component.WaitForElement("button[aria-pressed=true]");
    Assert.Empty(component.FindAll(".appearance-loader"));
    Assert.Equal(
      "dark",
      component.FindComponent<AppearanceProvider>().Instance.Theme
    );
  }

  [Fact]
  public async Task PendingSaveIsSingleAndFailureRetainsTheConfirmedTheme()
  {
    var writes = 0;
    var pending = new TaskCompletionSource<HttpResponseMessage>();
    await using var context = new ClientComponentContext(
      (request, _) =>
      {
        if (request.Method == HttpMethod.Get)
          return Task.FromResult(Response("light"));
        writes++;
        return pending.Task;
      }
    );
    var js = context.JSInterop.SetupModule(
      "./js/generated/shared/appearance.js"
    );
    js.SetupVoid("applyTheme", _ => true).SetVoidResult();
    var component = Render(context, Account("one"));
    component.WaitForElement("button[aria-pressed=true]");
    var provider = component.FindComponent<AppearanceProvider>();
    var save = provider.InvokeAsync(() => provider.Instance.SaveAsync("dark"));
    component.WaitForAssertion(() => Assert.Equal(1, writes));
    await provider.InvokeAsync(() => provider.Instance.SaveAsync("dark"));
    Assert.All(
      component.FindAll("button"),
      button => Assert.True(button.HasAttribute("disabled"))
    );
    pending.SetResult(new(HttpStatusCode.ServiceUnavailable));
    await save;
    component.WaitForElement("[role=alert]");
    Assert.Equal("light", provider.Instance.Theme);
    Assert.Equal(1, writes);
    Assert.DoesNotContain(
      js.Invocations,
      call =>
        call.Identifier == "applyTheme" && Equals(call.Arguments[0], "dark")
    );
  }

  [Fact]
  public async Task SuccessfulSaveAppliesServerValueAndDoesNotRemountChildren()
  {
    var writes = new List<AppearanceSettings>();
    await using var context = new ClientComponentContext(
      async (request, ct) =>
      {
        if (request.Method == HttpMethod.Get)
          return Response("dark");
        writes.Add(
          (await request.Content!.ReadFromJsonAsync<AppearanceSettings>(ct))!
        );
        return Response(writes[^1].Theme);
      }
    );
    var js = context.JSInterop.SetupModule(
      "./js/generated/shared/appearance.js"
    );
    js.SetupVoid("applyTheme", _ => true).SetVoidResult();
    var component = Render(context, Account("one"));
    component.WaitForElement("button[aria-pressed=true]");
    var control = component.FindComponent<ThemeControl>().Instance;
    await component.FindAll("button")[0].ClickAsync(new());
    Assert.Equal("light", Assert.Single(writes).Theme);
    component.WaitForElement(".settings-page__saved");
    Assert.Same(control, component.FindComponent<ThemeControl>().Instance);
    Assert.Equal("light", js.Invocations.Last().Arguments[0]);
  }

  [Theory]
  [InlineData(false)]
  [InlineData(true)]
  public async Task LateAccountReadOrSaveCannotChangeTheNextAccount(bool save)
  {
    var reads = 0;
    var pending = new TaskCompletionSource<HttpResponseMessage>();
    await using var context = new ClientComponentContext(
      (request, _) =>
      {
        if (request.Method == HttpMethod.Put)
          return pending.Task;
        reads++;
        return !save && reads == 1
          ? pending.Task
          : Task.FromResult(Response("light"));
      }
    );
    var js = context.JSInterop.SetupModule(
      "./js/generated/shared/appearance.js"
    );
    js.SetupVoid("applyTheme", _ => true).SetVoidResult();
    var component = Render(context, Account("one"));
    component.WaitForAssertion(() => Assert.Equal(1, reads));
    Task? saving = null;
    if (save)
    {
      component.WaitForElement("button");
      var provider = component.FindComponent<AppearanceProvider>();
      saving = provider.InvokeAsync(() => provider.Instance.SaveAsync("dark"));
    }
    component.Render(parameters =>
      parameters
        .Add(x => x.Value, Account("two"))
        .AddChildContent<AppearanceProvider>(provider =>
          provider.AddChildContent<ThemeControl>()
        )
    );
    component.WaitForAssertion(() => Assert.Equal(2, reads));
    component.WaitForElement("button[aria-pressed=true]");
    pending.SetResult(Response("dark", "celsius", "kilometers"));
    if (saving is not null)
      await saving;
    Assert.Equal(
      "both",
      component.FindComponent<AppearanceProvider>().Instance.Units.Distance
    );
    Assert.Equal(
      "celsius",
      component.FindComponent<AppearanceProvider>().Instance.Units.Temperature
    );
    component.WaitForAssertion(
      () =>
        Assert.Equal(
          "light",
          component.FindComponent<AppearanceProvider>().Instance.Theme
        )
    );
    Assert.DoesNotContain(
      js.Invocations,
      call =>
        call.Identifier == "applyTheme" && Equals(call.Arguments[0], "dark")
    );
  }

  [Fact]
  public async Task DispatcherCanOpenAppearanceWithoutLoadingAdminSettings()
  {
    await using var context = new ClientComponentContext(
      (_, _) =>
        throw new InvalidOperationException("No admin settings read allowed.")
    );
    var authorization = context.AddAuthorization();
    authorization.SetAuthorized("Dispatcher");
    authorization.SetRoles("Dispatch");
    var component = context.Render<SettingsPage>();
    Assert.Single(component.FindComponents<ThemeControl>());
    Assert.Empty(component.FindComponents<FleetSettings>());
  }

  [Fact]
  public async Task UnitChangesPublishAccountValuesWithoutChangingTheTheme()
  {
    var saved = new AppearanceSettings("dark", "both", "both");
    var writes = new List<AppearanceSettings>();
    await using var context = new ClientComponentContext(
      async (request, ct) =>
      {
        if (request.Method == HttpMethod.Put)
        {
          var update = (
            await request.Content!.ReadFromJsonAsync<AppearanceSettings>(ct)
          )!;
          writes.Add(update);
          saved = new(
            update.Theme,
            update.TemperatureUnit ?? saved.TemperatureUnit,
            update.DistanceUnit ?? saved.DistanceUnit
          );
        }
        return Response(saved.Theme, saved.TemperatureUnit, saved.DistanceUnit);
      }
    );
    context
      .JSInterop.SetupModule("./js/generated/shared/appearance.js")
      .SetupVoid("applyTheme", _ => true)
      .SetVoidResult();
    var component = Render(context, Account("one"));
    await component
      .WaitForElement("#personal-temperature")
      .ChangeAsync(new() { Value = "fahrenheit" });
    Assert.Equal(
      new[] { "fahrenheit", "celsius" },
      component
        .FindAll("#personal-temperature option")
        .Select(option => option.GetAttribute("value"))
    );
    await component
      .Find("#personal-distance")
      .ChangeAsync(new() { Value = "kilometers" });
    var provider = component.FindComponent<AppearanceProvider>();
    Assert.Equal("dark", provider.Instance.Theme);
    Assert.Equal("fahrenheit", provider.Instance.Units.Temperature);
    Assert.Equal("kilometers", provider.Instance.Units.Distance);
    Assert.Equal(
      "3,021\u00a0km",
      component.FindComponent<DistanceText>().Markup.Trim()
    );
    Assert.Equal(2, writes.Count);
    Assert.Null(writes[0].DistanceUnit);
    Assert.Null(writes[1].TemperatureUnit);
    Assert.Equal(
      "kilometers",
      ((IHtmlSelectElement)component.Find("#personal-distance")).Value
    );
  }

  [Fact]
  public async Task FailedUnitSaveRetainsTheConfirmedChoice()
  {
    await using var context = new ClientComponentContext(
      (request, _) =>
        Task.FromResult(
          request.Method == HttpMethod.Get
            ? Response("light", "celsius", "kilometers")
            : new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)
        )
    );
    context
      .JSInterop.SetupModule("./js/generated/shared/appearance.js")
      .SetupVoid("applyTheme", _ => true)
      .SetVoidResult();
    var component = Render(context, Account("one"));
    await component
      .WaitForElement("#personal-distance")
      .ChangeAsync(new() { Value = "miles" });
    component.WaitForElement("[role=alert]");
    Assert.Equal(
      "kilometers",
      component.FindComponent<AppearanceProvider>().Instance.Units.Distance
    );
    Assert.Equal(
      "kilometers",
      ((IHtmlSelectElement)component.Find("#personal-distance")).Value
    );
  }

  [Fact]
  public async Task LogoutRestoresLightAndRepeatedIdentityDoesNotReload()
  {
    var reads = 0;
    await using var context = new ClientComponentContext(
      (_, _) =>
      {
        reads++;
        return Task.FromResult(Response("dark"));
      }
    );
    var js = context.JSInterop.SetupModule(
      "./js/generated/shared/appearance.js"
    );
    js.SetupVoid("applyTheme", _ => true).SetVoidResult();
    var component = Render(context, Account("one"));
    component.WaitForElement("button[aria-pressed=true]");
    component.Render(parameters =>
      parameters
        .Add(x => x.Value, Account("one"))
        .AddChildContent<AppearanceProvider>(provider =>
          provider.AddChildContent<ThemeControl>()
        )
    );
    Assert.Equal(1, reads);
    component.Render(parameters =>
      parameters
        .Add(
          x => x.Value,
          Task.FromResult(new AuthenticationState(new ClaimsPrincipal()))
        )
        .AddChildContent<AppearanceProvider>(provider =>
          provider.AddChildContent<ThemeControl>()
        )
    );
    component.WaitForAssertion(
      () =>
        Assert.Equal(
          "light",
          component.FindComponent<AppearanceProvider>().Instance.Theme
        )
    );
    Assert.Equal("light", js.Invocations.Last().Arguments[0]);
    Assert.Equal(1, reads);
  }

  [Fact]
  public async Task FailedInitialReadStillShowsSettingsAndRetryRestoresTheme()
  {
    var reads = 0;
    await using var context = new ClientComponentContext(
      (request, _) =>
      {
        Assert.Equal(HttpMethod.Get, request.Method);
        return Task.FromResult(
          ++reads == 1
            ? new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)
            : Response("dark")
        );
      }
    );
    var js = context.JSInterop.SetupModule(
      "./js/generated/shared/appearance.js"
    );
    js.SetupVoid("applyTheme", _ => true).SetVoidResult();
    var component = Render(context, Account("one"));
    component.WaitForElement("[role=alert]");
    Assert.Equal(
      "light",
      component.FindComponent<AppearanceProvider>().Instance.Theme
    );
    await component.Find("[role=alert] button").ClickAsync(new());
    Assert.Equal(2, reads);
    Assert.Empty(component.FindAll("[role=alert]"));
    Assert.Equal("dark", js.Invocations.Last().Arguments[0]);
  }

  private static Task<AuthenticationState> Account(string id) =>
    Task.FromResult(
      new AuthenticationState(
        new ClaimsPrincipal(
          new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, id)], "Test")
        )
      )
    );

  private static IRenderedComponent<
    CascadingValue<Task<AuthenticationState>>
  > Render(ClientComponentContext context, Task<AuthenticationState> account) =>
    context.Render<CascadingValue<Task<AuthenticationState>>>(parameters =>
      parameters
        .Add(x => x.Value, account)
        .AddChildContent<AppearanceProvider>(provider =>
          provider.AddChildContent(builder =>
          {
            builder.OpenComponent<ThemeControl>(0);
            builder.CloseComponent();
            builder.OpenComponent<DistanceText>(1);
            builder.AddAttribute(2, "Miles", 1877d);
            builder.CloseComponent();
          })
        )
    );

  private static HttpResponseMessage Response(
    string theme,
    string? temperature = null,
    string? distance = null
  ) =>
    new(HttpStatusCode.OK)
    {
      Content = JsonContent.Create(
        new RequestResponseDTO<AppearanceSettings>
        {
          Success = true,
          Response = new(theme, temperature, distance),
        }
      ),
    };
}
