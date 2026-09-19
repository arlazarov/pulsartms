using Microsoft.JSInterop;

namespace Client.Services;

public sealed class PageVisibility : IAsyncDisposable
{
  private IJSObjectReference? module,
    observer;
  private DotNetObjectReference<PageVisibility>? reference;
  private Task? starting;
  private bool disposed;
  private CancellationTokenSource changed = new();
  public bool IsVisible { get; private set; } = true;
  public CancellationToken Changed => changed.Token;

  public Task StartAsync(IJSRuntime js) => starting ??= StartCoreAsync(js);

  private async Task StartCoreAsync(IJSRuntime js)
  {
    try
    {
      module = await js.InvokeAsync<IJSObjectReference>(
        "import",
        "./js/generated/shared/pageVisibility.js"
      );
      if (module is null || disposed)
        return;
      reference = DotNetObjectReference.Create(this);
      observer = await module.InvokeAsync<IJSObjectReference>(
        "observeVisibility",
        reference
      );
      if (observer is not null && !disposed)
        VisibilityChanged(
          await observer.InvokeAsync<bool?>("isVisible") ?? true
        );
    }
    catch (JSException) { }
  }

  [JSInvokable]
  public void VisibilityChanged(bool visible)
  {
    if (disposed || visible == IsVisible)
      return;
    IsVisible = visible;
    var previous = changed;
    changed = new();
    previous.Cancel();
    previous.Dispose();
  }

  public async ValueTask DisposeAsync()
  {
    if (disposed)
      return;
    disposed = true;
    changed.Cancel();
    try
    {
      // Awaited inside: a start that faulted with anything but a JSException
      // used to leave before the releases below ever ran.
      if (starting is not null)
        await starting;
      if (observer is not null)
      {
        await observer.InvokeVoidAsync("dispose");
        await observer.DisposeAsync();
      }
      if (module is not null)
        await module.DisposeAsync();
    }
    catch (JSDisconnectedException) { }
    finally
    {
      reference?.Dispose();
      changed.Dispose();
    }
  }
}
