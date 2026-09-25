using System.Collections.Concurrent;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.JSInterop;

namespace Client.Services;

// What a reader leaves behind on a page when they open a load, kept so
// that returning finds it as it was: the page's state in its own address
// (so browser Back and a reload read it), how far down it was scrolled,
// and a reply being written. Memory only, for this tab; cleared when the
// signed-in user changes, so nothing kept for one user or company shows
// for the next.
public sealed class ReturnPlaces : IDisposable, IAsyncDisposable
{
  private const string Module = "./js/generated/shared/returnPlace.js";
  private readonly IJSRuntime _js;
  private readonly AuthenticationStateProvider _auth;
  private readonly ConcurrentDictionary<Guid, string> _drafts = new();
  private Task<IJSObjectReference>? _module;

  public ReturnPlaces(IJSRuntime js, AuthenticationStateProvider auth)
  {
    _js = js;
    _auth = auth;
    _auth.AuthenticationStateChanged += OnAuthenticationChanged;
  }

  public Task ReflectAsync(string url) => CallAsync("reflect", url);

  public Task ArmAsync() => CallAsync("arm");

  public Task DisarmAsync() => CallAsync("disarm");

  // Held only from leaving until the conversation is opened again, which
  // takes it; leaving with nothing written keeps nothing and removes
  // nothing, so a page closing late cannot erase a reply kept since.
  public void KeepDraft(Guid conversation, string text)
  {
    if (text.Trim().Length > 0)
      _drafts[conversation] = text;
  }

  public string TakeDraft(Guid conversation) =>
    _drafts.TryRemove(conversation, out var text) ? text : "";

  private void OnAuthenticationChanged(Task<AuthenticationState> state)
  {
    _drafts.Clear();
    _ = CallAsync("forget");
  }

  private async Task CallAsync(string name, params object?[] arguments)
  {
    try
    {
      _module ??= _js.InvokeAsync<IJSObjectReference>("import", Module)
        .AsTask();
      await (await _module).InvokeVoidAsync(name, arguments);
    }
    catch (JSDisconnectedException) { }
    catch (JSException) { }
  }

  public void Dispose() =>
    _auth.AuthenticationStateChanged -= OnAuthenticationChanged;

  public async ValueTask DisposeAsync()
  {
    Dispose();
    if (_module is not null)
      try
      {
        await (await _module).DisposeAsync();
      }
      catch (JSDisconnectedException) { }
      catch (JSException) { }
  }
}
