using System.Globalization;
using System.Net;
using Client.Models.DTO.Dispatch;
using Client.Services;
using Microsoft.AspNetCore.Components;

namespace Client.Pages.Dispatch;

public partial class DispatchActivityPanel : IDisposable
{
  [Inject]
  private ApiService Api { get; set; } = default!;

  [Parameter]
  public Guid DispatchId { get; set; }

  [Parameter]
  public EventCallback<bool> DraftChanged { get; set; }

  private CancellationTokenSource _identity = new();
  private Guid _loadId;
  private long _revision;
  private IReadOnlyList<DispatchActivityItem> _items = [];
  private IReadOnlyList<DispatchActivityItem> _openItems = [];
  private long? _next;
  private long? _nextOpen;
  private int _openCount;
  private bool _older;
  private bool _olderOpen;
  private bool _reading;
  private bool _writing;
  private bool _loaded;
  private bool _disposed;
  private bool _reportedDraft;
  private string _text = "";
  private AddDispatchActivityUpdate? _pendingAdd;
  private DispatchActivityItem? _pendingIssue;
  private ResolveDispatchActivityUpdate? _pendingResolve;
  private string? _error;
  private string? _notice;
  private bool Busy => _reading || _writing;
  private bool Unconfirmed =>
    _pendingAdd is not null || _pendingIssue is not null;
  private string Id => $"dispatch-activity-{DispatchId}";

  protected override async Task OnParametersSetAsync()
  {
    if (_loadId == DispatchId)
      return;
    _identity.Cancel();
    _identity.Dispose();
    _identity = new();
    _loadId = DispatchId;
    _items = [];
    _openItems = [];
    _loaded = false;
    _reading = false;
    _writing = false;
    _pendingAdd = null;
    _pendingIssue = null;
    _pendingResolve = null;
    _text = "";
    _error = null;
    _notice = null;
    await PublishDraftAsync();
    await ReadAsync();
  }

  private bool Current(CancellationTokenSource owner) =>
    !_disposed && owner == _identity && !owner.IsCancellationRequested;

  private async Task ReadAsync(long? before = null, bool openOnly = false)
  {
    if (Busy || Unconfirmed || DispatchId == Guid.Empty || _disposed)
      return;
    var owner = _identity;
    _reading = true;
    _error = null;
    var query = $"?openOnly={openOnly.ToString().ToLowerInvariant()}";
    if (before.HasValue)
      query += $"&beforeRevision={before.Value}";
    var response = await Api.GetAsync<DispatchActivityPage>(
      $"api/dispatch/{DispatchId}/activity{query}",
      owner.Token
    );
    if (!Current(owner))
      return;
    _reading = false;
    if (
      !response.Success
      || response.Response is not { } page
      || page.DispatchId != DispatchId
    )
    {
      _error = "Notes could not be loaded. Reload notes to try again.";
      return;
    }
    _loaded = true;
    _revision = page.Revision;
    _openCount = page.OpenCount;
    _openItems = page.OpenItems;
    _nextOpen = page.NextOpenBeforeRevision;
    _olderOpen = openOnly && before.HasValue;
    if (!openOnly)
    {
      _items = page.Items;
      _next = page.NextBeforeRevision;
      _older = before.HasValue;
    }
  }

  private Task RefreshAsync() => ReadAsync();

  private Task OlderAsync() => ReadAsync(_next);

  private Task OlderOpenAsync() => ReadAsync(_nextOpen, true);

  private Task NewestOpenAsync() => ReadAsync(null, true);

  private async Task AddAsync()
  {
    if (Busy || !_loaded || _pendingIssue is not null)
      return;
    _error = null;
    _notice = null;
    if (string.IsNullOrWhiteSpace(_text) || _text.Length > 4000)
    {
      _error = "Enter a note of up to 4,000 characters.";
      return;
    }
    var update =
      _pendingAdd
      ?? new AddDispatchActivityUpdate(
        Guid.NewGuid(),
        _revision,
        "note",
        _text.Trim(),
        null,
        null,
        false
      );
    var owner = _identity;
    _writing = true;
    _pendingAdd = update;
    await PublishDraftAsync();
    if (!Current(owner))
      return;
    var response = await Api.PostAsync<
      AddDispatchActivityUpdate,
      DispatchActivityItem
    >($"api/dispatch/{DispatchId}/activity", update, owner.Token);
    if (!Current(owner))
      return;
    _writing = false;
    if (
      !response.Success
      || response.Response is not { } item
      || item.DispatchId != DispatchId
    )
    {
      if (
        response.HttpStatusCode
        is HttpStatusCode.BadRequest
          or HttpStatusCode.Conflict
          or HttpStatusCode.Forbidden
          or HttpStatusCode.Unauthorized
      )
      {
        _pendingAdd = null;
        _error =
          response.HttpStatusCode == HttpStatusCode.Conflict
            ? "Notes changed. Reload notes, then save your note."
            : "The note could not be saved. Check your access.";
      }
      else
        _error = "Save could not be confirmed. Retry this note to confirm it.";
      return;
    }
    _pendingAdd = null;
    _text = "";
    _notice = "Note added.";
    await PublishDraftAsync();
    await ReadAsync();
  }

  private async Task ResolveAsync(DispatchActivityItem issue)
  {
    if (
      Busy
      || _pendingAdd is not null
      || _pendingIssue is not null && _pendingIssue.Id != issue.Id
    )
      return;
    _error = null;
    _notice = null;
    var owner = _identity;
    var update =
      _pendingResolve
      ?? new ResolveDispatchActivityUpdate(Guid.NewGuid(), issue.Revision);
    _pendingIssue = issue;
    _pendingResolve = update;
    _writing = true;
    await PublishDraftAsync();
    if (!Current(owner))
      return;
    var response = await Api.PostAsync<
      ResolveDispatchActivityUpdate,
      DispatchActivityItem
    >(
      $"api/dispatch/{DispatchId}/activity/{issue.Id}/resolve",
      update,
      owner.Token
    );
    if (!Current(owner))
      return;
    _writing = false;
    if (
      !response.Success
      || response.Response is not { } item
      || item.DispatchId != DispatchId
      || item.Id != issue.Id
    )
    {
      if (
        response.HttpStatusCode
        is HttpStatusCode.BadRequest
          or HttpStatusCode.Conflict
          or HttpStatusCode.Forbidden
          or HttpStatusCode.Unauthorized
          or HttpStatusCode.NotFound
      )
      {
        _pendingIssue = null;
        _pendingResolve = null;
        _error = "The issue changed or access was denied. Reload notes.";
        await PublishDraftAsync();
      }
      else
        _error = "Resolution could not be confirmed. Retry Resolve.";
      return;
    }
    _pendingIssue = null;
    _pendingResolve = null;
    _notice = "Issue resolved. The original note is retained.";
    await PublishDraftAsync();
    await ReadAsync();
  }

  private async Task PublishDraftAsync()
  {
    if (_disposed)
      return;
    var draft = _text.Length > 0 || Unconfirmed;
    if (_reportedDraft == draft)
      return;
    _reportedDraft = draft;
    await DraftChanged.InvokeAsync(draft);
  }

  private static string LocalTime(DateTime at) =>
    DateTime
      .SpecifyKind(at, DateTimeKind.Utc)
      .ToLocalTime()
      .ToString("MMM d · hh:mm tt", CultureInfo.InvariantCulture);

  public void Dispose()
  {
    _disposed = true;
    _identity.Cancel();
    _identity.Dispose();
  }
}
