using Client.Models.DTO.Storage;
using Client.Services;
using Microsoft.AspNetCore.Components;

namespace Client.Pages.Settings;

// The company's readable layout for stored files. The example comes from
// the server, so it shows exactly what the saved template produces.
public partial class StorageLayoutSettings : IDisposable
{
  [Inject]
  private ApiService Api { get; set; } = default!;

  private StorageLayoutView? _layout;
  private string _loadsFolder = "",
    _template = "",
    _suffix = "",
    _inbox = "";
  private bool _loading,
    _saving,
    _saved,
    _disposed;
  private string? _error;
  private readonly CancellationTokenSource _lifetime = new();

  private bool Changed =>
    _layout is not null
    && (
      _loadsFolder != _layout.LoadsFolder
      || _template != _layout.LoadTemplate
      || _suffix != _layout.CancelledSuffix
      || _inbox != _layout.InboxFolder
    );

  protected override Task OnInitializedAsync() => LoadAsync();

  private async Task LoadAsync()
  {
    _loading = true;
    _error = null;
    var result = await Api.GetAsync<StorageLayoutView>(
      "api/storage/layout",
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
    Show(result.Response);
  }

  private async Task SaveAsync()
  {
    if (_layout is null || _saving || _disposed)
      return;
    _saving = true;
    _saved = false;
    _error = null;
    var result = await Api.PutAsync<StorageLayoutUpdate, StorageLayoutView>(
      "api/storage/layout",
      new(_loadsFolder, _template, _suffix, _inbox, _layout.Revision),
      _lifetime.Token
    );
    if (_disposed)
      return;
    _saving = false;
    if (!result.Success || result.Response is null)
    {
      // The draft stays as typed.
      _error = result.ErrorMessage;
      return;
    }
    Show(result.Response);
    _saved = true;
  }

  private void Show(StorageLayoutView layout)
  {
    _layout = layout;
    _loadsFolder = layout.LoadsFolder;
    _template = layout.LoadTemplate;
    _suffix = layout.CancelledSuffix;
    _inbox = layout.InboxFolder;
  }

  public void Dispose()
  {
    _disposed = true;
    _lifetime.Cancel();
    _lifetime.Dispose();
  }
}
