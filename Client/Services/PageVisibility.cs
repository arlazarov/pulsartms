using Microsoft.JSInterop;

namespace Client.Services;

public interface IPageVisibility
{
  bool Hidden { get; }
  event Action? Changed;
  Task StartAsync();
}

public static class PageVisibilityPacing
{
  // A hidden tab cannot show updates and browsers already throttle it; polling drops to this cadence.
  public static readonly TimeSpan HiddenInterval = TimeSpan.FromSeconds(60);

  public static TimeSpan Interval(this IPageVisibility visibility, TimeSpan visible) =>
    visibility.Hidden && HiddenInterval > visible ? HiddenInterval : visible;

  // Dispose before the timer: a late visibility change must not touch a disposed timer.
  public static IDisposable Pace(this IPageVisibility visibility, PeriodicTimer timer, TimeSpan visible)
  {
    void Apply() => timer.Period = visibility.Interval(visible);
    Apply();
    visibility.Changed += Apply;
    return new Unsubscribe(() => visibility.Changed -= Apply);
  }

  private sealed class Unsubscribe(Action dispose) : IDisposable
  {
    public void Dispose() => dispose();
  }
}

public sealed class PageVisibility(IJSRuntime js) : IPageVisibility, IAsyncDisposable
{
  private IJSObjectReference? _module;
  private IJSObjectReference? _watcher;
  private DotNetObjectReference<PageVisibility>? _self;
  public bool Hidden { get; private set; }
  public event Action? Changed;

  public async Task StartAsync()
  {
    if (_self is not null) return;
    _self = DotNetObjectReference.Create(this);
    try
    {
      _module = await js.InvokeAsync<IJSObjectReference>("import", "./js/generated/shared/visibility.js");
      _watcher = await _module.InvokeAsync<IJSObjectReference>("watch", _self);
    }
    catch (JSException) { } // Without the module every page keeps its visible cadence.
  }

  [JSInvokable]
  public void OnVisibilityChanged(bool hidden)
  {
    if (Hidden == hidden) return;
    Hidden = hidden;
    Changed?.Invoke();
  }

  public async ValueTask DisposeAsync()
  {
    try
    {
      if (_watcher is not null) { await _watcher.InvokeVoidAsync("dispose"); await _watcher.DisposeAsync(); }
      if (_module is not null) await _module.DisposeAsync();
    }
    catch (JSDisconnectedException) { }
    catch (JSException) { }
    _self?.Dispose();
  }
}
