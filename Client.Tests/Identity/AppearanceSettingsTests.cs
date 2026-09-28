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
    component.WaitForElement("#personal-temperature");
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
    component.WaitForElement("#personal-temperature");
    var provider = component.FindComponent<AppearanceProvider>();
    var save = provider.InvokeAsync(() => provider.Instance.SaveAsync("dark"));
    component.WaitForAssertion(() => Assert.Equal(1, writes));
    await provider.InvokeAsync(() => provider.Instance.SaveAsync("dark"));
    Assert.True(
      component.Find(".settings-page__form").HasAttribute("disabled")
    );
    pending.SetResult(new(HttpStatusCode.ServiceUnavailable));
    await save;
    component.WaitForElement("[role=alert]");
    Assert.Equal("light", provider.Instance.Theme);
    Assert.Equal(1, writes);
    // The saved light choice is kept, but only dark is ever painted.
    Assert.All(
      js.Invocations.Where(call => call.Identifier == "applyTheme"),
      call => Assert.Equal("dark", call.Arguments[0])
    );
  }

  // Light is withdrawn for now (the owner, September 28): no theme choice
  // is offered, an account that saved light is painted dark, and its
  // saved choice is sent back unchanged with a units change - never
  // overwritten with dark.
  [Fact]
  public async Task ASavedLightChoiceIsKeptButOnlyDarkIsPainted()
  {
    var writes = new List<AppearanceSettings>();
    await using var context = new ClientComponentContext(
      async (request, ct) =>
      {
        if (request.Method == HttpMethod.Get)
          return Response("light");
        writes.Add(
          (await request.Content!.ReadFromJsonAsync<AppearanceSettings>(ct))!
        );
        return Response(writes[^1].Theme, distance: writes[^1].DistanceUnit);
      }
    );
    var js = context.JSInterop.SetupModule(
      "./js/generated/shared/appearance.js"
    );
    js.SetupVoid("applyTheme", _ => true).SetVoidResult();
    var component = Render(context, Account("one"));
    var distance = component.WaitForElement("#personal-distance");
    var control = component.FindComponent<ThemeControl>().Instance;
    await distance.ChangeAsync(new() { Value = "kilometers" });
    Assert.Empty(component.FindAll("[aria-label=Theme], [aria-pressed]"));
    Assert.DoesNotContain("Light", component.Markup);
    var write = Assert.Single(writes);
    Assert.Equal("light", write.Theme);
    Assert.Null(write.TemperatureUnit);
    component.WaitForElement(".settings-page__saved");
    Assert.Same(control, component.FindComponent<ThemeControl>().Instance);
    var provider = component.FindComponent<AppearanceProvider>().Instance;
    Assert.Equal("light", provider.Theme);
    Assert.Equal("dark", provider.AppliedTheme);
    Assert.NotEmpty(js.Invocations);
    Assert.All(
      js.Invocations.Where(call => call.Identifier == "applyTheme"),
      call => Assert.Equal("dark", call.Arguments[0])
    );
  }

  // An account that never chose a theme reads as empty (migration 79).
  // It loads without an error, is painted dark, and a units change sends
  // dark: the server takes only light or dark, and dark is what that
  // account already sees.
  [Fact]
  public async Task AnUnchosenThemeLoadsAndAUnitsChangeSendsDark()
  {
    var writes = new List<AppearanceSettings>();
    await using var context = new ClientComponentContext(
      async (request, ct) =>
      {
        if (request.Method == HttpMethod.Get)
          return Response("");
        writes.Add(
          (await request.Content!.ReadFromJsonAsync<AppearanceSettings>(ct))!
        );
        return Response(writes[^1].Theme, distance: writes[^1].DistanceUnit);
      }
    );
    var js = context.JSInterop.SetupModule(
      "./js/generated/shared/appearance.js"
    );
    js.SetupVoid("applyTheme", _ => true).SetVoidResult();
    var component = Render(context, Account("one"));
    await component
      .WaitForElement("#personal-distance")
      .ChangeAsync(new() { Value = "miles" });
    Assert.Empty(component.FindAll("[role=alert]"));
    Assert.Equal("dark", Assert.Single(writes).Theme);
    component.WaitForElement(".settings-page__saved");
    Assert.Equal(
      "dark",
      component.FindComponent<AppearanceProvider>().Instance.AppliedTheme
    );
    Assert.All(
      js.Invocations.Where(call => call.Identifier == "applyTheme"),
      call => Assert.Equal("dark", call.Arguments[0])
    );
  }

  // Until the account's own theme has been read a units change is not
  // sent: it would carry the dark default over a saved light choice.
  [Fact]
  public async Task NoSaveBeforeTheAccountsThemeIsKnown()
  {
    var writes = 0;
    await using var context = new ClientComponentContext(
      (request, _) =>
      {
        if (request.Method != HttpMethod.Get)
          writes++;
        return Task.FromResult(
          new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)
        );
      }
    );
    context
      .JSInterop.SetupModule("./js/generated/shared/appearance.js")
      .SetupVoid("applyTheme", _ => true)
      .SetVoidResult();
    var component = Render(context, Account("one"));
    component.WaitForElement("[role=alert]");
    var provider = component.FindComponent<AppearanceProvider>();
    await provider.InvokeAsync(
      () => provider.Instance.SaveAsync("dark", distance: "miles")
    );
    Assert.Equal(0, writes);
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
      component.WaitForElement("#personal-temperature");
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
    component.WaitForElement("#personal-temperature");
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
    Assert.All(
      js.Invocations.Where(call => call.Identifier == "applyTheme"),
      call => Assert.Equal("dark", call.Arguments[0])
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
  public async Task LogoutRestoresTheDarkDefaultAndRepeatedIdentityDoesNotReload()
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
    component.WaitForElement("#personal-temperature");
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
    // Dark is the default once no account is signed in (the owner,
    // September 27; it was light).
    component.WaitForAssertion(
      () =>
        Assert.Equal(
          "dark",
          component.FindComponent<AppearanceProvider>().Instance.Theme
        )
    );
    Assert.Equal("dark", js.Invocations.Last().Arguments[0]);
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
    // The dark default stands while the account's choice cannot be read.
    Assert.Equal(
      "dark",
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
