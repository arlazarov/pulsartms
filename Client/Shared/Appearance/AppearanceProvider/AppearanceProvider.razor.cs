using System.Security.Claims;
using Client.Models;
using Client.Models.DTO.Identity;
using Client.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.JSInterop;

namespace Client.Shared.Appearance.AppearanceProvider;

public partial class AppearanceProvider : IAsyncDisposable
{
  [Inject]
  private ApiService Api { get; set; } = default!;

  [Inject]
  private IJSRuntime JS { get; set; } = default!;

  [CascadingParameter]
  private Task<AuthenticationState> Authentication { get; set; } = default!;

  [Parameter]
  public RenderFragment? ChildContent { get; set; }

  // Light is withdrawn for now (the owner, September 28): the page is
  // always painted dark. Theme stays the account's saved choice, which is
  // kept and sent back unchanged with a units change, so turning light on
  // again restores every account's own theme.
  private const bool LightThemeAvailable = false;

  public string Theme { get; private set; } = "light";

  public string AppliedTheme =>
    LightThemeAvailable && Theme == "light" ? "light" : "dark";

  // The theme a units change sends back: the saved choice as it is, or
  // dark for an account that never chose (stored empty; the server takes
  // only light or dark, and dark is what that account already sees).
  public string ThemeToKeep => Theme is "light" ? "light" : "dark";
  public DisplayUnits Units { get; private set; } = DisplayUnits.Default;
  public bool Busy { get; private set; }
  public string? Error { get; private set; }
  public bool Saved { get; private set; }
  private string? _account;
  private bool _initialized,
    _ready,
    _disposed,
    _themeKnown;
  private long _generation;
  private CancellationTokenSource _accountLifetime = new();
  private Task<IJSObjectReference>? _module;

  protected override async Task OnParametersSetAsync()
  {
    var authentication = Authentication;
    var principal = (await authentication).User;
    if (_disposed || authentication != Authentication)
      return;
    var account =
      principal.Identity?.IsAuthenticated == true
        ? principal.FindFirst(ClaimTypes.NameIdentifier)?.Value
        : null;
    if (_initialized && account == _account)
      return;
    _initialized = true;
    _account = account;
    _accountLifetime.Cancel();
    _accountLifetime.Dispose();
    _accountLifetime = new();
    var generation = ++_generation;
    _ready = false;
    _themeKnown = false;
    Busy = false;
    Saved = false;
    Error = null;
    // Dark is the default (the owner, September 27) and, for now, the
    // only theme painted (index.html paints dark first).
    Theme = "dark";
    Units = DisplayUnits.Default;
    if (account is null)
      await ApplyAsync(generation);
    if (!IsCurrent(generation))
      return;
    if (account is not null)
      await ReloadAsync();
    if (IsCurrent(generation))
      _ready = true;
  }

  public async Task ReloadAsync()
  {
    if (_disposed || Busy || _account is null)
      return;
    var generation = _generation;
    Busy = true;
    Saved = false;
    Error = null;
    StateHasChanged();
    using var timeout = CancellationTokenSource.CreateLinkedTokenSource(
      _accountLifetime.Token
    );
    timeout.CancelAfter(TimeSpan.FromSeconds(10));
    var result = await Api.GetAsync<AppearanceSettings>(
      "api/settings/appearance",
      timeout.Token
    );
    if (!IsCurrent(generation))
      return;
    // An account that never chose a theme reads as empty (migration 79):
    // it is known, and painted dark.
    if (
      result.Success
      && result.Response is { Theme: null or "" or "light" or "dark" } settings
    )
    {
      Theme = settings.Theme ?? "";
      _themeKnown = true;
      ApplyUnits(settings);
      await ApplyAsync(generation);
    }
    else
      Error = "Could not load your personal preferences. Please retry.";
    if (!IsCurrent(generation))
      return;
    Busy = false;
    StateHasChanged();
  }

  public async Task SaveAsync(
    string theme,
    string? temperature = null,
    string? distance = null
  )
  {
    // A save sends the theme too: until the account's own choice has been
    // read, a units change would overwrite it with the default.
    if (
      _disposed
      || Busy
      || _account is null
      || !_themeKnown
      || theme is not ("light" or "dark")
      || temperature is not (null or "fahrenheit" or "celsius")
      || distance is not (null or "miles" or "kilometers" or "both")
      || (
        theme == Theme
        && Error is null
        && (temperature is null || temperature == Units.Temperature)
        && (distance is null || distance == Units.Distance)
      )
    )
      return;
    var generation = _generation;
    Busy = true;
    Saved = false;
    Error = null;
    StateHasChanged();
    var result = await Api.PutAsync<AppearanceSettings, AppearanceSettings>(
      "api/settings/appearance",
      new(theme, temperature, distance),
      _accountLifetime.Token
    );
    if (!IsCurrent(generation))
      return;
    if (result.Success && result.Response?.Theme is "light" or "dark")
    {
      Theme = result.Response.Theme;
      ApplyUnits(result.Response);
      await ApplyAsync(generation);
      if (!IsCurrent(generation))
        return;
      Saved = Error is null;
    }
    else
      Error = "Could not save your personal preferences. Please retry.";
    Busy = false;
    StateHasChanged();
  }

  private bool IsCurrent(long generation) =>
    !_disposed && generation == _generation;

  private void ApplyUnits(AppearanceSettings settings) =>
    Units = new DisplayUnits(
      settings.TemperatureUnit ?? DisplayUnits.Default.Temperature,
      settings.DistanceUnit ?? "both"
    ).Normalize();

  private async Task ApplyAsync(long generation)
  {
    try
    {
      _module ??= JS.InvokeAsync<IJSObjectReference>(
          "import",
          "./js/generated/shared/appearance.js"
        )
        .AsTask();
      var module = await _module;
      if (IsCurrent(generation))
        await module.InvokeVoidAsync("applyTheme", AppliedTheme);
    }
    catch (JSException)
    {
      if (_module?.IsFaulted == true)
        _module = null;
      if (IsCurrent(generation))
        Error = "Could not apply your theme. Please retry.";
    }
  }

  public async ValueTask DisposeAsync()
  {
    _disposed = true;
    _accountLifetime.Cancel();
    _accountLifetime.Dispose();
    if (_module is null)
      return;
    try
    {
      var module = await _module;
      await module.DisposeAsync();
    }
    catch (JSException) { }
  }
}
