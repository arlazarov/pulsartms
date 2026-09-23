using Client.Models.DTO;
using Client.Services;
using Microsoft.AspNetCore.Components;

namespace Client.Pages.Settings;

// Whether a prepared fuel plan may be sent without a dispatcher pressing
// Send plan. Saved with the other fleet-wide dispatch settings, under the
// same revision, and only as an explicit choice.
public partial class FuelSendingSettings : IDisposable
{
  [Inject]
  private ApiService Api { get; set; } = default!;

  private DispatchSettingsState? _settings;
  private bool _automatic,
    _loading,
    _saving,
    _saved,
    _disposed;
  private string? _error;
  private readonly CancellationTokenSource _lifetime = new();

  protected override Task OnInitializedAsync() => LoadAsync();

  private async Task LoadAsync()
  {
    _loading = true;
    _error = null;
    var result = await Api.GetAsync<DispatchSettingsState>(
      "api/settings/dispatch",
      _lifetime.Token
    );
    if (_disposed)
      return;
    _loading = false;
    if (!result.Success || result.Response is null)
    {
      _error = result.ErrorMessage;
      return;
    }
    _settings = result.Response;
    _automatic = _settings.AutomaticFuelSending;
  }

  private void OnChanged(ChangeEventArgs args)
  {
    _automatic = args.Value is true;
    _saved = false;
  }

  private async Task SaveAsync()
  {
    if (_settings is null || _loading || _saving || _disposed)
      return;
    _saving = true;
    _saved = false;
    _error = null;
    var result = await Api.PutAsync<
      DispatchSettingsUpdate,
      DispatchSettingsState
    >(
      "api/settings/dispatch",
      new(
        _settings.LoadNumberPrefix,
        _settings.Revision,
        AutomaticFuelSending: _automatic
      ),
      _lifetime.Token
    );
    if (_disposed)
      return;
    _saving = false;
    if (!result.Success || result.Response is null)
    {
      _error = result.ErrorMessage;
      return;
    }
    _settings = result.Response;
    _automatic = _settings.AutomaticFuelSending;
    _saved = true;
  }

  public void Dispose()
  {
    _disposed = true;
    _lifetime.Cancel();
    _lifetime.Dispose();
  }
}
