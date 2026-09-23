using System.Security.Claims;
using System.Text.Json;
using Client.Models.DTO.Messaging;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.JSInterop;

namespace Client.Services;

// "Something changed" for the messaging views, from one server stream per
// browser and account. Views subscribe while they are shown; the first
// subscriber joins the tab to the cross-tab channel
// (Scripts/shared/messagingChannel.ts) under the signed-in account and
// session, and whichever tab is elected reads the stream through the app's
// own authenticated client and passes each signal on. When the channel
// cannot be loaded, this tab reads its own stream and signals only itself.
// After every (re)connect it sends "resync", so a view reads everything
// again rather than trusting that no signal was lost; while the stream is
// down it sends a "poll" tick at most every 30 seconds and reconnects with
// backoff. A change of account or sign-out leaves and joins again.
public sealed class MessagingSignals : IAsyncDisposable
{
  public static readonly TimeSpan PollEvery = TimeSpan.FromSeconds(30);
  private static readonly TimeSpan FirstBackoff = TimeSpan.FromSeconds(2);
  private static readonly TimeSpan MaximumBackoff = TimeSpan.FromSeconds(60);

  private readonly ApiService _api;
  private readonly IJSRuntime _js;
  private readonly AuthenticationStateProvider _auth;
  private readonly TokenStorageService _tokens;
  private readonly TimeProvider _time;
  private readonly SemaphoreSlim _gate = new(1, 1);

  public event Action<MessagingSignal>? Changed;

  private IJSObjectReference? _module;
  private DotNetObjectReference<MessagingSignals>? _self;
  private CancellationTokenSource? _leading;
  private bool _joined;
  private int _subscribers;

  public MessagingSignals(
    ApiService api,
    IJSRuntime js,
    AuthenticationStateProvider auth,
    TokenStorageService tokens,
    TimeProvider time
  )
  {
    _api = api;
    _js = js;
    _auth = auth;
    _tokens = tokens;
    _time = time;
    _auth.AuthenticationStateChanged += OnAuthenticationChanged;
  }

  public async Task JoinAsync()
  {
    if (_subscribers++ > 0)
      return;
    await RestartAsync();
  }

  public async Task LeaveAsync()
  {
    if (_subscribers == 0 || --_subscribers > 0)
      return;
    await _gate.WaitAsync();
    try
    {
      await StopAsync();
    }
    finally
    {
      _gate.Release();
    }
  }

  private void OnAuthenticationChanged(Task<AuthenticationState> state)
  {
    if (_subscribers > 0)
      _ = RestartAsync();
  }

  private async Task RestartAsync()
  {
    await _gate.WaitAsync();
    try
    {
      await StopAsync();
      if (_subscribers == 0 || await ScopeAsync() is not { } scope)
        return;
      try
      {
        _module ??= await _js.InvokeAsync<IJSObjectReference>(
          "import",
          "./js/generated/shared/messagingChannel.js"
        );
        _self ??= DotNetObjectReference.Create(this);
        await _module.InvokeVoidAsync("join", _self, scope);
        _joined = true;
      }
      catch (JSException)
      {
        // No channel: this tab reads its own stream.
        await Lead();
      }
    }
    finally
    {
      _gate.Release();
    }
  }

  private async Task StopAsync()
  {
    _leading?.Cancel();
    _leading = null;
    if (!_joined || _module is null)
      return;
    _joined = false;
    try
    {
      await _module.InvokeVoidAsync("leave");
    }
    catch (JSException) { }
    catch (JSDisconnectedException) { }
  }

  // The signed-in account and this sign-in's session: tabs share a leader
  // only when both match. Null when nobody is signed in.
  private async Task<string?> ScopeAsync()
  {
    try
    {
      var user = (await _auth.GetAuthenticationStateAsync()).User;
      var account = user.FindFirst(ClaimTypes.NameIdentifier)?.Value;
      var session = await _tokens.GetSessionAsync();
      return
        user.Identity?.IsAuthenticated == true
        && Guid.TryParse(account, out var id)
        && session is not null
        ? $"{id:N}:{session.Id:N}"
        : null;
    }
    catch (JSException)
    {
      return null;
    }
  }

  // Whether this tab reads the stream for the browser (or for itself, when
  // it has no channel).
  public bool IsLeading => _leading is { IsCancellationRequested: false };

  [JSInvokable]
  public void Receive(string kind, string? id) =>
    Changed?.Invoke(
      new(kind, Guid.TryParse(id, out var conversation) ? conversation : null)
    );

  // The leading tab's unread count, relayed to every tab of the account.
  [JSInvokable]
  public void Unread(int count, bool more, UnreadMark[] latest) =>
    Changed?.Invoke(new("unread", null, new(count, more, latest)));

  // "read" from the tab that marked a conversation read, and the leader's
  // "unread" count: to every tab of the account through the channel, or to
  // this tab alone without one.
  public Task AnnounceReadAsync() => AnnounceAsync(new { kind = "read" });

  public Task AnnounceUnreadAsync(UnreadCount count) =>
    AnnounceAsync(
      new
      {
        kind = "unread",
        count = count.Conversations,
        more = count.More,
        latest = count.Latest,
      },
      count
    );

  private async Task AnnounceAsync(object signal, UnreadCount? count = null)
  {
    if (_joined && _module is not null)
      try
      {
        await _module.InvokeVoidAsync("post", signal);
        return;
      }
      catch (JSException) { }
    Changed?.Invoke(
      count is null ? new("read", null) : new("unread", null, count)
    );
  }

  [JSInvokable]
  public Task Lead()
  {
    _leading?.Cancel();
    _leading = new CancellationTokenSource();
    _ = ReadAsync(_leading.Token);
    return Task.CompletedTask;
  }

  private async Task ReadAsync(CancellationToken ct)
  {
    var backoff = FirstBackoff;
    while (!ct.IsCancellationRequested)
    {
      try
      {
        using var response = await _api.OpenStreamAsync(
          "api/messaging/events",
          ct
        );
        if (response is not null)
        {
          backoff = FirstBackoff;
          await PostAsync("resync", null, ct);
          await using var stream = await response.Content.ReadAsStreamAsync(ct);
          using var reader = new StreamReader(stream);
          while (
            !ct.IsCancellationRequested
            && await reader.ReadLineAsync(ct) is { } line
          )
            if (Change(line) is { } id)
              await PostAsync("change", id, ct);
        }
      }
      catch (OperationCanceledException) when (ct.IsCancellationRequested)
      {
        return;
      }
      catch (Exception ex)
        when (ex is HttpRequestException or IOException or JSException)
      {
        // The stream dropped; views keep up through the poll ticks below.
      }
      var until = _time.GetUtcNow() + backoff;
      backoff = TimeSpan.FromTicks(
        Math.Min(backoff.Ticks * 2, MaximumBackoff.Ticks)
      );
      try
      {
        while (true)
        {
          await PostAsync("poll", null, ct);
          // Measured once, after the tick: a slow tick may have used up the
          // whole wait.
          var left = until - _time.GetUtcNow();
          if (left <= TimeSpan.Zero)
            break;
          await Task.Delay(left < PollEvery ? left : PollEvery, _time, ct);
        }
      }
      catch (OperationCanceledException)
      {
        return;
      }
    }
  }

  // "data: {conversationId, revision}" lines carry a change; comments and
  // anything else are ignored.
  public static string? Change(string line)
  {
    if (!line.StartsWith("data:", StringComparison.Ordinal))
      return null;
    try
    {
      using var json = JsonDocument.Parse(line[5..]);
      return
        json.RootElement.ValueKind == JsonValueKind.Object
        && json.RootElement.TryGetProperty("conversationId", out var id)
        && id.ValueKind == JsonValueKind.String
        && Guid.TryParse(id.GetString(), out var parsed)
        && parsed != Guid.Empty
        ? parsed.ToString()
        : null;
    }
    catch (JsonException)
    {
      return null;
    }
  }

  // Through the channel when joined, otherwise to this tab only. A channel
  // that fails is not the stream's fault, so the signal reaches this tab.
  // A reader stopped by a leave or a sign-in change is not awaited, so it
  // is fenced here: the channel may already belong to the next join.
  private async Task PostAsync(string kind, string? id, CancellationToken ct)
  {
    if (ct.IsCancellationRequested)
      return;
    if (_joined && _module is not null)
      try
      {
        await _module.InvokeVoidAsync("post", new { kind, id });
        return;
      }
      catch (JSException) { }
    Receive(kind, id);
  }

  public async ValueTask DisposeAsync()
  {
    _auth.AuthenticationStateChanged -= OnAuthenticationChanged;
    await StopAsync();
    if (_module is not null)
      try
      {
        await _module.DisposeAsync();
      }
      catch (JSDisconnectedException) { }
    _self?.Dispose();
    _gate.Dispose();
  }
}

public sealed record MessagingSignal(
  string Kind,
  Guid? ConversationId,
  UnreadCount? Unread = null
);
