using Client.Models.DTO;
using Client.Services;
using Microsoft.AspNetCore.Components;

namespace Client.Layout;

public partial class MainLayout : IDisposable
{
  [Inject]
  private ApiService Api { get; set; } = default!;

  [Inject]
  private NavigationManager Navigation { get; set; } = default!;

  private string? _pagePath;
  private string? _pageTransition;
  private DispatchSettingsState? _dispatchSettings;
  private Action<DispatchSettingsState> _settingsChanged = default!;
  private readonly CancellationTokenSource _lifetime = new();
  private bool _disposed;

  protected override async Task OnInitializedAsync()
  {
    _pagePath = CurrentPagePath;
    _settingsChanged = ApplySettings;
    var result = await Api.GetAsync<DispatchSettingsState>(
      "api/settings/dispatch",
      _lifetime.Token
    );
    if (!_disposed && result.Success && result.Response is { } settings)
      ApplySettings(settings);
  }

  private string CurrentPagePath =>
    Navigation.ToAbsoluteUri(Navigation.Uri).AbsolutePath;

  protected override void OnParametersSet()
  {
    var path = CurrentPagePath;
    if (string.Equals(path, _pagePath, StringComparison.Ordinal))
      return;

    _pagePath = path;
    // Restart only the visual effect; never key or remount the page subtree.
    _pageTransition = _pageTransition == "a" ? "b" : "a";
  }

  private void ApplySettings(DispatchSettingsState settings)
  {
    if (_disposed || settings.Revision < (_dispatchSettings?.Revision ?? 0))
      return;
    _dispatchSettings = settings;
    StateHasChanged();
  }

  public void Dispose()
  {
    _disposed = true;
    _lifetime.Cancel();
    _lifetime.Dispose();
  }
}
