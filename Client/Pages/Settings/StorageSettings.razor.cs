using Client.Models.DTO.Storage;
using Client.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;

namespace Client.Pages.Settings;

// Where the company keeps its files. Connecting an account leaves PulsR for
// the provider's consent page and comes back to Settings with an outcome.
public partial class StorageSettings : IDisposable
{
  [Inject]
  private ApiService Api { get; set; } = default!;

  [Inject]
  private NavigationManager Navigation { get; set; } = default!;

  [Inject]
  private IJSRuntime JS { get; set; } = default!;

  private IJSObjectReference? _picker;

  private Models.DTO.Storage.StorageSettings? _settings;
  private bool _loading,
    _busy,
    _disposed;
  private string? _error,
    _outcome;
  private Guid? _confirming;
  private readonly CancellationTokenSource _lifetime = new();

  private bool _disconnectedNote =>
    _settings?.Connections?.Any(x => x.State == "disconnected") == true;

  protected override Task OnInitializedAsync()
  {
    var query = new Uri(Navigation.Uri).Query;
    _outcome =
      query.Contains("storage=connected") ? "The storage was connected."
      : query.Contains("storage=failed")
        ? "The storage was not connected. See its message below."
      : query.Contains("storage=invalid")
        ? "That connection link was already used or has expired."
      : null;
    return LoadAsync();
  }

  private async Task LoadAsync()
  {
    _loading = true;
    _error = null;
    var result = await Api.GetAsync<Models.DTO.Storage.StorageSettings>(
      "api/storage",
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
  }

  private Task MakeDefaultAsync(StorageConnectionView connection) =>
    ChangeAsync($"api/storage/connections/{connection.Id}/default", connection);

  private Task DisconnectAsync(StorageConnectionView connection) =>
    ChangeAsync(
      $"api/storage/connections/{connection.Id}/disconnect",
      connection
    );

  private async Task CheckAsync(StorageConnectionView connection)
  {
    if (!Begin())
      return;
    var result = await Api.PostAsync<object, StorageConnectionView>(
      $"api/storage/connections/{connection.Id}/check",
      new { },
      _lifetime.Token
    );
    await EndAsync(result.Success ? null : result.ErrorMessage);
  }

  private async Task ChangeAsync(string url, StorageConnectionView connection)
  {
    if (!Begin())
      return;
    var result = await Api.PostAsync<
      StorageRevisionRequest,
      StorageConnectionView
    >(url, new(connection.Revision), _lifetime.Token);
    await EndAsync(result.Success ? null : result.ErrorMessage);
  }

  // The provider's own picker, so the administrator chooses among their
  // drives, shared ones included. Only the folder id comes back; the server
  // checks it can hold new files before using it.
  private async Task ChooseFolderAsync(StorageConnectionView connection)
  {
    if (!Begin())
      return;
    var session = await Api.PostAsync<object, StoragePickerSession>(
      $"api/storage/connections/{connection.Id}/picker",
      new { },
      _lifetime.Token
    );
    if (_disposed)
      return;
    if (!session.Success || session.Response is null)
    {
      await EndAsync(
        session.ErrorMessage ?? "The folder picker could not open."
      );
      return;
    }
    string? folder;
    try
    {
      _picker ??= await JS.InvokeAsync<IJSObjectReference>(
        "import",
        _lifetime.Token,
        "./js/generated/settings/storagePicker.js"
      );
      folder = await _picker.InvokeAsync<string?>(
        "pickFolder",
        _lifetime.Token,
        session.Response
      );
    }
    catch (JSException)
    {
      await EndAsync("The folder picker could not open.");
      return;
    }
    if (_disposed)
      return;
    if (folder is null)
    {
      _busy = false;
      return;
    }
    var chosen = await Api.PostAsync<StorageRootRequest, StorageConnectionView>(
      $"api/storage/connections/{connection.Id}/root",
      new(folder, connection.Revision),
      _lifetime.Token
    );
    await EndAsync(chosen.Success ? null : chosen.ErrorMessage);
  }

  private async Task ConnectAsync(StorageKindView kind)
  {
    if (!Begin())
      return;
    var result = await Api.PostAsync<
      StorageConnectRequest,
      StorageConnectionStart
    >("api/storage/connections", new(kind.Kind, kind.Name), _lifetime.Token);
    if (_disposed)
      return;
    if (
      result.Success
      && Uri.TryCreate(
        result.Response?.AuthorizationUrl,
        UriKind.Absolute,
        out var url
      )
      && url.Scheme == Uri.UriSchemeHttps
    )
    {
      Navigation.NavigateTo(url.ToString(), forceLoad: true);
      return;
    }
    await EndAsync(result.ErrorMessage ?? "The connection could not start.");
  }

  private bool Begin()
  {
    if (_busy || _disposed)
      return false;
    _busy = true;
    _error = null;
    _outcome = null;
    return true;
  }

  private async Task EndAsync(string? error)
  {
    if (_disposed)
      return;
    _confirming = null;
    await LoadAsync();
    _busy = false;
    if (error is not null)
      _error = string.IsNullOrWhiteSpace(error)
        ? "The request failed. Please retry."
        : error;
  }

  private string KindName(string kind) =>
    _settings?.Kinds.FirstOrDefault(x => x.Kind == kind)?.Name ?? kind;

  private static string StateText(StorageConnectionView connection) =>
    connection.IsDefault && connection.State == "connected"
      ? "Default"
      : connection.State switch
      {
        "connected" => "Connected",
        "failed" => "Needs attention",
        "needs-root" => "Choose a folder",
        "disconnected" => "Disconnected",
        _ => connection.State,
      };

  private static string StateClass(StorageConnectionView connection) =>
    connection.State switch
    {
      "connected" => "is-connected",
      "failed" or "needs-root" => "is-failed",
      _ => "",
    };

  public void Dispose()
  {
    _disposed = true;
    _lifetime.Cancel();
    _lifetime.Dispose();
    _ = _picker?.DisposeAsync().AsTask();
  }
}
