using Client.Models.DTO.Planning;
using Client.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Forms;

namespace Client.Pages.Settings;

public partial class FleetSettings : IDisposable
{
  [Inject]
  private ApiService Api { get; set; } = default!;
  private PlanningPreferences _form = new();
  private EditContext? _context;
  private long _revision;
  private bool _loading,
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
    var result = await Api.GetAsync<PlanningSettingsState>(
      "api/settings/planning",
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
    _revision = result.Response.Revision;
    SetForm(result.Response.Preferences);
  }

  private void SetForm(PlanningPreferences preferences)
  {
    if (_context is not null)
      _context.OnFieldChanged -= OnFieldChanged;
    _form = preferences;
    _context = new(_form);
    _context.OnFieldChanged += OnFieldChanged;
    _saved = false;
  }

  private void OnFieldChanged(object? sender, FieldChangedEventArgs args)
  {
    _saved = false;
  }

  private async Task SaveAsync()
  {
    if (_loading || _saving || _disposed)
      return;
    _saving = true;
    _saved = false;
    _error = null;
    var result = await Api.PutAsync<
      PlanningSettingsUpdate,
      PlanningSettingsState
    >("api/settings/planning", new(_form, _revision), _lifetime.Token);
    if (_disposed)
      return;
    _saving = false;
    if (!result.Success || result.Response is null)
    {
      _error = result.ErrorMessage;
      return;
    }
    _revision = result.Response.Revision;
    SetForm(result.Response.Preferences);
    _saved = true;
  }

  public void Dispose()
  {
    _disposed = true;
    _lifetime.Cancel();
    _lifetime.Dispose();
    if (_context is not null)
      _context.OnFieldChanged -= OnFieldChanged;
  }
}
