using System.Globalization;
using Client.Models.DTO.Messaging;
using Client.Services;
using Microsoft.AspNetCore.Components;

namespace Client.Pages.Messages;

// Searching the whole history, not the pages shown: words, days and a load,
// in the open conversation or in every conversation of the driver group.
// Typing waits a moment, a newer search cancels the one before, and an
// answer for an earlier search is dropped. Days are the dispatcher's own:
// the browser's time zone goes with them.
public partial class MessageSearch : IDisposable
{
  public const int DelayMilliseconds = 300;

  [Parameter]
  public Guid? ConversationId { get; set; }

  [Parameter]
  public EventCallback<MessageSearchHit> OnOpen { get; set; }

  [Parameter]
  public EventCallback OnClose { get; set; }

  [Inject]
  private ApiService Api { get; set; } = default!;

  private string _text = "",
    _load = "";
  private DateOnly? _from,
    _to;
  private bool _allChats,
    _searching,
    _disposed;
  private string? _error;
  private List<MessageSearchHit>? _hits;
  private MessageCursor? _next;
  private int _generation;
  private CancellationTokenSource? _running;
  private Guid? _scoped;

  private bool HasCriteria =>
    _text.Trim().Length > 0
    || _from is not null
    || _to is not null
    || Load is not null;

  private int? Load =>
    int.TryParse(
      _load.Trim(),
      NumberStyles.None,
      CultureInfo.InvariantCulture,
      out var n
    )
    && n > 0
      ? n
      : null;

  protected override async Task OnParametersSetAsync()
  {
    if (_scoped == ConversationId)
      return;
    _scoped = ConversationId;
    if (HasCriteria && !_allChats)
      await SearchAsync(null, delay: false);
  }

  private async Task ChangedAsync() => await SearchAsync(null, delay: true);

  private async Task ScopeAsync(bool all)
  {
    _allChats = all;
    await SearchAsync(null, delay: false);
  }

  private Task MoreAsync() => SearchAsync(_next, delay: false);

  private async Task SearchAsync(MessageCursor? after, bool delay)
  {
    var generation = ++_generation;
    _running?.Cancel();
    _running?.Dispose();
    _running = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
    var ct = _running.Token;
    _error = null;
    if (!HasCriteria)
    {
      _hits = null;
      _next = null;
      _searching = false;
      return;
    }
    try
    {
      if (delay)
        await Task.Delay(DelayMilliseconds, ct);
    }
    catch (OperationCanceledException)
    {
      return;
    }
    if (_disposed || generation != _generation)
      return;
    _searching = true;
    StateHasChanged();
    var result = await Api.GetAsync<MessageSearchView>(Path(after), ct);
    if (_disposed || generation != _generation)
      return;
    _searching = false;
    if (!result.Success || result.Response is not { } view)
    {
      _error = result.ErrorMessage;
      return;
    }
    _hits = after is null ? [.. view.Hits] : [.. _hits ?? [], .. view.Hits];
    _next = view.Next;
  }

  private string Path(MessageCursor? after)
  {
    var query = new List<string>();
    if (!_allChats && ConversationId is { } conversation)
      query.Add($"conversationId={conversation}");
    if (_text.Trim() is { Length: > 0 } text)
      query.Add($"text={Uri.EscapeDataString(text)}");
    if (_from is { } from)
      query.Add($"from={from:yyyy-MM-dd}");
    if (_to is { } to)
      query.Add($"to={to:yyyy-MM-dd}");
    if ((_from ?? _to) is not null)
      query.Add($"timeZone={Uri.EscapeDataString(TimeZone)}");
    if (Load is { } load)
      query.Add($"load={load}");
    if (after is not null)
      query.Add(
        $"afterSentAt={Uri.EscapeDataString(after.SentAt.ToString("O"))}"
          + $"&afterCreatedAt={Uri.EscapeDataString(after.CreatedAt.ToString("O"))}"
          + $"&afterId={after.Id}"
      );
    return "api/messaging/search?" + string.Join("&", query);
  }

  // The browser's own IANA zone, such as America/Toronto: WebAssembly
  // takes the local zone from the browser.
  private static string TimeZone => TimeZoneInfo.Local.Id;

  private readonly CancellationTokenSource _lifetime = new();

  private static string Who(MessageSearchHit hit) =>
    hit.DriverName ?? hit.Participant;

  private static string When(MessageSearchHit hit) =>
    hit
      .SentAt.ToLocalTime()
      .ToString("MMM d, HH:mm", CultureInfo.InvariantCulture);

  public void Dispose()
  {
    _disposed = true;
    _running?.Cancel();
    _running?.Dispose();
    _lifetime.Cancel();
    _lifetime.Dispose();
  }
}
