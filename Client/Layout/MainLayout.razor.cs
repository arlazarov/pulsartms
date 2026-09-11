using Client.Models.DTO;
using Client.Services;
using Microsoft.AspNetCore.Components;

namespace Client.Layout;

public partial class MainLayout : IDisposable
{
    [Inject] private ApiService Api { get; set; } = default!;
    private DispatchSettingsState? _dispatchSettings;
    private Action<DispatchSettingsState> _settingsChanged = default!;
    private readonly CancellationTokenSource _lifetime = new();
    private bool _disposed;

    protected override async Task OnInitializedAsync()
    {
        _settingsChanged = ApplySettings;
        var result = await Api.GetAsync<DispatchSettingsState>("api/settings/dispatch", _lifetime.Token);
        if (!_disposed && result.Success && result.Response is { } settings) ApplySettings(settings);
    }

    private void ApplySettings(DispatchSettingsState settings)
    {
        if (_disposed || settings.Revision < (_dispatchSettings?.Revision ?? 0)) return;
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
