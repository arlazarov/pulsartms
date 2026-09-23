using Client.Models.DTO.Messaging;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.JSInterop;

namespace Client.Services;

// The unread count every page shows, and browser notifications for it.
// Each tab reads the count once when it starts and when the account
// changes. After that only the tab leading the account's stream reads it:
// a burst of signals becomes one read after a short pause, the result goes
// to every tab of the account, and only that tab may show a notification,
// so a browser with many tabs reads and notifies once. A "read" from any
// tab asks the leader to count again.
//
// Every result carries the account generation it was asked for and is
// checked against it again right before each effect - showing, relaying,
// notifying - since the account can change, or the service be disposed,
// while any of them is awaited.
public sealed class MessagingNotices : IAsyncDisposable
{
  public static readonly TimeSpan Coalesce = TimeSpan.FromMilliseconds(500);
  private const string Module = "./js/generated/shared/messagingNotices.js";

  private readonly ApiService _api;
  private readonly MessagingSignals _signals;
  private readonly AuthenticationStateProvider _auth;
  private readonly IJSRuntime _js;
  private readonly TimeProvider _time;

  // The revision each conversation's latest driver message arrived at, as
  // last counted: a notice is due only when one rises. Reads, claims and
  // replies never raise it; a late message with an older time does.
  private readonly Dictionary<Guid, long> _seen = [];
  private IJSObjectReference? _notices;
  private int _subscribers,
    _account,
    _read;
  private bool _counting,
    _again,
    _baseline,
    _disposed;

  public event Action? Changed;

  public UnreadCount? Current { get; private set; }

  public MessagingNotices(
    ApiService api,
    MessagingSignals signals,
    AuthenticationStateProvider auth,
    IJSRuntime js,
    TimeProvider time
  )
  {
    _api = api;
    _signals = signals;
    _auth = auth;
    _js = js;
    _time = time;
  }

  public async Task JoinAsync()
  {
    if (_disposed || _subscribers++ > 0)
      return;
    _signals.Changed += OnSignal;
    _auth.AuthenticationStateChanged += OnAuthenticationChanged;
    await _signals.JoinAsync();
    await ReadAsync(_account, announce: false);
  }

  public async Task LeaveAsync()
  {
    if (_subscribers == 0 || --_subscribers > 0)
      return;
    Stop();
    await _signals.LeaveAsync();
  }

  private void Stop()
  {
    _signals.Changed -= OnSignal;
    _auth.AuthenticationStateChanged -= OnAuthenticationChanged;
    _account++;
    _read++;
  }

  private void OnAuthenticationChanged(Task<AuthenticationState> state)
  {
    var account = ++_account;
    Current = null;
    _seen.Clear();
    _baseline = false;
    Changed?.Invoke();
    _ = ReadAsync(account, announce: false);
  }

  private void OnSignal(MessagingSignal signal)
  {
    if (signal.Kind == "unread")
    {
      if (signal.Unread is { } count)
      {
        Remember(count);
        Show(count);
      }
      return;
    }
    if (_signals.IsLeading)
      _ = CountAsync(_account);
  }

  // One read at a time; signals during it make exactly one more.
  private async Task CountAsync(int account)
  {
    if (_counting)
    {
      _again = true;
      return;
    }
    _counting = true;
    try
    {
      do
      {
        _again = false;
        await Task.Delay(Coalesce, _time);
        if (!Live(account) || !_signals.IsLeading)
          return;
        await ReadAsync(account, announce: true);
      } while (_again && Live(account));
    }
    finally
    {
      _counting = false;
    }
  }

  private bool Live(int account) => !_disposed && account == _account;

  private async Task ReadAsync(int account, bool announce)
  {
    var read = ++_read;
    var result = await _api.GetAsync<UnreadCount>("api/messaging/unread");
    if (
      !Live(account)
      || read != _read
      || !result.Success
      || result.Response is not { } count
    )
      return;
    var newer = _baseline && count.Latest.Any(Raised);
    Remember(count);
    Show(count);
    if (!announce)
      return;
    await _signals.AnnounceUnreadAsync(count);
    if (newer && Live(account))
      await NotifyAsync(account, count);
  }

  private bool Raised(UnreadMark mark) =>
    !_seen.TryGetValue(mark.ConversationId, out var seen)
    || mark.Revision > seen;

  // The first count after a start or an account change is the baseline
  // and never notifies.
  private void Remember(UnreadCount count)
  {
    foreach (var mark in count.Latest)
      if (Raised(mark))
        _seen[mark.ConversationId] = mark.Revision;
    _baseline = true;
  }

  private void Show(UnreadCount count)
  {
    Current = count;
    Changed?.Invoke();
  }

  private async Task NotifyAsync(int account, UnreadCount count)
  {
    try
    {
      _notices ??= await _js.InvokeAsync<IJSObjectReference>("import", Module);
      if (!Live(account))
        return;
      await _notices.InvokeAsync<bool>(
        "notify",
        count.Conversations,
        count.More
      );
    }
    catch (JSException)
    {
      // Notifications are an extra; the count still shows.
    }
  }

  public async ValueTask DisposeAsync()
  {
    _disposed = true;
    Stop();
    if (_notices is not null)
      try
      {
        await _notices.DisposeAsync();
      }
      catch (JSDisconnectedException) { }
  }
}
