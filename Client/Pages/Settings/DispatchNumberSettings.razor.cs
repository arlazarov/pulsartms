using Client.Shared.Dispatch;
using System.ComponentModel.DataAnnotations;
using Client.Models.DTO;
using Client.Services;
using Client.Shared;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Forms;

namespace Client.Pages.Settings;

public partial class DispatchNumberSettings : IDisposable
{
    [Inject] private ApiService Api { get; set; } = default!;
    [CascadingParameter(Name = "DispatchSettingsChanged")] public Action<DispatchSettingsState>? SettingsChanged { get; set; }
    private PrefixForm _form = new();
    private EditContext? _context;
    private long _revision;
    private bool _loading, _saving, _saved, _disposed;
    private string? _error;
    private readonly CancellationTokenSource _lifetime = new();
    private string Example => LoadNumberDisplay.Format(1373, _form.LoadNumberPrefix?.Trim());

    protected override Task OnInitializedAsync() => LoadAsync();

    private async Task LoadAsync()
    {
        _loading = true;
        _error = null;
        var result = await Api.GetAsync<DispatchSettingsState>("api/settings/dispatch", _lifetime.Token);
        if (_disposed) return;
        _loading = false;
        if (!result.Success || result.Response is null) { _error = result.ErrorMessage; return; }
        SetForm(result.Response);
        SettingsChanged?.Invoke(result.Response);
    }

    private void SetForm(DispatchSettingsState settings)
    {
        if (_context is not null) _context.OnFieldChanged -= OnFieldChanged;
        _revision = settings.Revision;
        _form = new() { LoadNumberPrefix = settings.LoadNumberPrefix };
        _context = new(_form);
        _context.OnFieldChanged += OnFieldChanged;
        _saved = false;
    }

    private void OnFieldChanged(object? sender, FieldChangedEventArgs args) => _saved = false;

    private async Task SaveAsync()
    {
        if (_loading || _saving || _disposed) return;
        _saving = true;
        _saved = false;
        _error = null;
        var request = new DispatchSettingsUpdate(_form.LoadNumberPrefix?.Trim() ?? "", _revision);
        var result = await Api.PutAsync<DispatchSettingsUpdate, DispatchSettingsState>("api/settings/dispatch", request, _lifetime.Token);
        if (_disposed) return;
        _saving = false;
        if (!result.Success || result.Response is null) { _error = result.ErrorMessage; return; }
        SetForm(result.Response);
        _saved = true;
        SettingsChanged?.Invoke(result.Response);
    }

    public void Dispose()
    {
        _disposed = true;
        _lifetime.Cancel();
        _lifetime.Dispose();
        if (_context is not null) _context.OnFieldChanged -= OnFieldChanged;
    }

    private sealed class PrefixForm
    {
        [StringLength(16, ErrorMessage = "Use at most 16 characters for the prefix.")]
        public string? LoadNumberPrefix { get; set; }
    }
}
