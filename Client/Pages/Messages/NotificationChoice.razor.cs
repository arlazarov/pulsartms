using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;

namespace Client.Pages.Messages;

// The dispatcher's opt-in to browser notifications for new driver
// messages, in this browser. Nothing is asked until the dispatcher
// presses the button; a browser without notifications shows nothing.
public partial class NotificationChoice : IAsyncDisposable
{
  private const string Module = "./js/generated/shared/messagingNotices.js";

  [Inject]
  private IJSRuntime JS { get; set; } = default!;

  private IJSObjectReference? _module;
  private string _permission = "unsupported";
  private bool _enabled,
    _busy;

  protected override async Task OnInitializedAsync()
  {
    try
    {
      _module = await JS.InvokeAsync<IJSObjectReference>("import", Module);
      _permission = await _module.InvokeAsync<string>("permission");
      _enabled = await _module.InvokeAsync<bool>("enabled");
    }
    catch (JSException)
    {
      _permission = "unsupported";
    }
  }

  private async Task RequestAsync()
  {
    if (_module is null)
      return;
    _busy = true;
    try
    {
      _permission = await _module.InvokeAsync<string>("request");
      _enabled = await _module.InvokeAsync<bool>("enabled");
    }
    catch (JSException)
    {
      _permission = "unsupported";
    }
    _busy = false;
  }

  private async Task ToggleAsync()
  {
    if (_module is null)
      return;
    _enabled = !_enabled;
    try
    {
      await _module.InvokeVoidAsync("setEnabled", _enabled);
    }
    catch (JSException)
    {
      _enabled = !_enabled;
    }
  }

  public async ValueTask DisposeAsync()
  {
    if (_module is not null)
      try
      {
        await _module.DisposeAsync();
      }
      catch (JSDisconnectedException) { }
  }
}
