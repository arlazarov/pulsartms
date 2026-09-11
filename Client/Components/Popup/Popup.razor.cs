using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;

namespace Client.Components.Popup;

public partial class Popup : IAsyncDisposable
{
  [Inject]
  protected IJSRuntime JS { get; set; } = default!;

  [Parameter]
  public bool IsOpen { get; set; }

  [Parameter]
  public string? Title { get; set; }

  [Parameter]
  public RenderFragment? ChildContent { get; set; }

  [Parameter]
  public RenderFragment? Footer { get; set; }

  [Parameter]
  public EventCallback OnClose { get; set; }

  private IJSObjectReference? _module;

  private bool _scrollLocked;

  protected override async Task OnAfterRenderAsync(bool firstRender)
  {
    _module ??= await JS.InvokeAsync<IJSObjectReference>("import", "./js/generated/shared/popup.js");

    if (IsOpen && !_scrollLocked)
    {
      await _module.InvokeVoidAsync("lockScroll");

      _scrollLocked = true;
    }
    else if (!IsOpen && _scrollLocked)
    {
      await UnlockScrollAsync();
    }
  }

  protected Task CloseAsync()
  {
    return OnClose.InvokeAsync();
  }

  private async Task UnlockScrollAsync()
  {
    if (_module is null)
      return;

    await _module.InvokeVoidAsync("unlockScroll");

    _scrollLocked = false;
  }

  public async ValueTask DisposeAsync()
  {
    if (_scrollLocked)
    {
      await UnlockScrollAsync();
    }

    if (_module is not null)
    {
      await _module.DisposeAsync();
    }

    GC.SuppressFinalize(this);
  }
}
