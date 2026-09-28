using System.Security.Claims;
using Client.Models.DTO.Messaging;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.JSInterop;

namespace Client.Services;

// "Something changed" for the messaging views, from one reader per browser
// and account. Views subscribe while they are shown; the first subscriber
// joins the tab to the cross-tab channel (Scripts/shared/messagingChannel.ts)
// under the signed-in account and session, and whichever tab is elected
// asks the server what changed through the app's own authenticated client
// and passes each signal on. When the channel cannot be loaded, this tab
// asks for itself and signals only itself.
//
// Each request answers as soon as a change commits, or empty after the
// server's wait (MessagingMailboxes.Wait, 20 seconds), and names the
// mailbox to ask with next. It is a request that ends because Firebase
// Hosting, in front of the API, held a streamed response back until it
// ended: the stream this replaced never spoke, and inbound messages showed
// only on the 30-second poll. A new mailbox, or a server that lost signals,
// sends "resync", so a view reads everything again rather than trusting
// that nothing was missed. While requests fail it sends a "poll" tick at
// most every 30 seconds and retries with backoff. A change of account or
// sign-out leaves and joins again.
public sealed class MessagingSignals : IAsyncDisposable
{
  public static readonly TimeSpan PollEvery = TimeSpan.FromSeconds(30);

  // The server's wait plus a margin: an answer later than this counts as
  // the connection being down.
  public static readonly TimeSpan AnswerTimeout = TimeSpan.FromSeconds(35);

  // A mailbox lives on one API instance only, so a change committed on
  // another never reaches it. While answers come, the leader asks every
  // view to check itself once this long has passed since the last ask, so
  // such a change shows within about a minute and a half.
  public static readonly TimeSpan RepairEvery = TimeSpan.FromSeconds(60);
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

  // The scope this tab joined under, and the mailbox it last read under
  // which scope. A tab that leaves the messaging views and comes back asks
  // with its mailbox: its last request may still hold that mailbox on the
  // server - Hosting does not pass the abort on - and opening a new one
  // each time used up the account's share (503s of September 27).
  private string? _scope;
  private string? _mailboxScope;
  private Guid? _mailbox;

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
      _scope = scope;
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
  public void Unread(int count, bool more, long newest) =>
    Changed?.Invoke(new("unread", null, new(count, more, newest)));

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
        newest = count.Newest,
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
    _ = ReadAsync(_scope, _leading.Token);
    return Task.CompletedTask;
  }

  private async Task ReadAsync(string? scope, CancellationToken ct)
  {
    var backoff = FirstBackoff;
    Guid? mailbox =
      scope is not null && scope == _mailboxScope ? _mailbox : null;
    var replaced = 0;
    var repaired = _time.GetUtcNow();
    while (!ct.IsCancellationRequested)
    {
      var changes = await AskAsync(mailbox, ct);
      if (ct.IsCancellationRequested)
        return;
      if (changes is not null)
      {
        // A mailbox the server no longer had: whatever changed before the
        // new one opened was queued for nobody.
        replaced =
          mailbox is not null && changes.Mailbox != mailbox ? replaced + 1 : 0;
        // Only an answer for the mailbox asked with is a healthy one: a run
        // of replacements keeps doubling the wait below.
        if (replaced == 0)
          backoff = FirstBackoff;
        if (changes.Resync || changes.Mailbox != mailbox)
        {
          await PostAsync("resync", null, ct);
          repaired = _time.GetUtcNow();
        }
        mailbox = changes.Mailbox;
        (_mailbox, _mailboxScope) = (mailbox, scope);
        foreach (var id in changes.Conversations)
          await PostAsync("change", id.ToString(), ct);
        if (_time.GetUtcNow() - repaired >= RepairEvery)
        {
          repaired = _time.GetUtcNow();
          await PostAsync("poll", null, ct);
        }
        // Replaced again and again - requests reaching another instance
        // each time - would read everything in a loop; it waits instead.
        if (replaced < 2)
          continue;
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

  // Null when no answer came in time or the server refused.
  private async Task<MessagingChanges?> AskAsync(
    Guid? mailbox,
    CancellationToken ct
  )
  {
    using var late = new CancellationTokenSource(AnswerTimeout, _time);
    using var linked = CancellationTokenSource.CreateLinkedTokenSource(
      ct,
      late.Token
    );
    var answer = await _api.GetAsync<MessagingChanges>(
      mailbox is { } known
        ? $"api/messaging/changes?mailbox={known}"
        : "api/messaging/changes",
      linked.Token
    );
    return answer is { Success: true, Response: { } changes } ? changes : null;
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
