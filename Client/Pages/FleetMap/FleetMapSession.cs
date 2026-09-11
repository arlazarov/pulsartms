using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;

namespace Client.Pages.FleetMap;

internal sealed class FleetMapSession<T>(IJSRuntime js, T callbacks) : IAsyncDisposable where T : class
{
  private IJSObjectReference? _module;
  private DotNetObjectReference<T>? _callbacks;
  private Task? _startTask;
  private Task? _disposeTask;
  private bool _disposed;
  public IJSObjectReference? Map { get; private set; }

  public Task StartAsync(ElementReference element, string? apiKey, object options)
  {
    if (_disposed) return Task.CompletedTask;
    if (_startTask is { IsCompleted: false }) return _startTask;
    if (Map is not null) return Task.CompletedTask;
    return _startTask = StartCoreAsync(element, apiKey, options);
  }

  private async Task StartCoreAsync(ElementReference element, string? apiKey, object options)
  {
    try
    {
      _module ??= await ImportAsync();
      if (_disposed) return;
      _callbacks ??= DotNetObjectReference.Create(callbacks);
      Map = await _module.InvokeAsync<IJSObjectReference>("createFleetMap", element, apiKey, _callbacks);
      if (_disposed) return;
      await Map.InvokeVoidAsync("setOptions", options);
    }
    catch
    {
      await ReleaseMapAsync();
      throw;
    }
  }

  private async Task<IJSObjectReference> ImportAsync()
  {
    const string path = "./js/generated/fleetMap/fleetMap.js";
    try { return await js.InvokeAsync<IJSObjectReference>("import", path); }
    catch (JSException ex) when (ex.Message.Contains("Failed to fetch dynamically imported module", StringComparison.Ordinal))
    {
      return await js.InvokeAsync<IJSObjectReference>("import", $"{path}?retry={Guid.NewGuid():N}");
    }
  }

  private async Task ReleaseMapAsync()
  {
    var map = Map;
    Map = null;
    if (map is null) return;
    try { await map.InvokeVoidAsync("dispose"); }
    catch (JSException) { }
    finally { await map.DisposeAsync(); }
  }

  public ValueTask DisposeAsync() => new(_disposeTask ??= DisposeCoreAsync());

  private async Task DisposeCoreAsync()
  {
    _disposed = true;
    try
    {
      // Finish acquiring in-flight JS resources before releasing their references.
      if (_startTask is not null)
      {
        try { await _startTask; }
        catch (JSException) { }
      }
    }
    finally
    {
      try { await ReleaseMapAsync(); }
      finally
      {
        try { if (_module is not null) await _module.DisposeAsync(); }
        catch (JSDisconnectedException) { }
        finally { _module = null; _callbacks?.Dispose(); _callbacks = null; }
      }
    }
  }
}
