using System.Globalization;
using Client.Models.DTO.Messaging;
using Client.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.JSInterop;

namespace Client.Pages.Messages;

// Conversations with drivers. The page reads the inbox and the open
// conversation; the browser's one messaging stream tells it when to read
// them again, and a late answer for an earlier read never replaces a newer
// one. A reply keeps its retry key until it is sent, so pressing Send twice
// sends it once, and a reply refused as stale can be confirmed.
public partial class Messages : IAsyncDisposable
{
  [Parameter]
  public Guid? Id { get; set; }

  [Inject]
  private ApiService Api { get; set; } = default!;

  [Inject]
  private MessagingSignals Signals { get; set; } = default!;

  [Inject]
  private IJSRuntime JS { get; set; } = default!;

  [CascadingParameter]
  private Task<AuthenticationState>? Authentication { get; set; }

  [Inject]
  private ChosenDriverGroup DriverGroup { get; set; } = default!;

  [Inject]
  private NavigationManager Navigation { get; set; } = default!;

  // The list as shown: the first page, and the pages the dispatcher asked
  // for below it. Next continues it; a new search or filter starts over.
  private List<ConversationSummary>? _conversations;
  private InboxCursor? _next;
  private bool _extended,
    _loadingMore;
  private string _search = "",
    _searched = "";
  private int _listVersion,
    _moreRead;
  private ConversationView? _thread;
  private List<MessageView> _messages = [];
  private IReadOnlyList<MessageTemplateView> _templates = [];
  private bool _unreadOnly,
    _stale,
    _disposed;

  // Each opening of a conversation is an editor session. A send on its way
  // is kept by conversation with the session it came from: its
  // conversation stays busy, even after leaving and coming back, until it
  // answers, and its answer touches the draft, files and refusal only in
  // the session that sent it.
  private int _editor;
  private readonly Dictionary<Guid, int> _sending = [];
  private bool _busy => Id is { } id && _sending.ContainsKey(id);
  private string _draft = "";
  private Guid? _draftKey;
  private string? _error,
    _refusal,
    _template;
  private readonly Dictionary<int, string> _parameters = [];
  private readonly List<StagedFile> _staged = [];
  private int _inboxRead,
    _threadRead;

  // Rereads, one at a time: the open conversation's per conversation, the
  // list's for the page. A demand that comes while one is on its way is
  // served by one more read after it.
  private readonly CoalescedReads<Guid> _threads = new();
  private readonly CoalescedReads<bool> _lists = new();

  // Files being read into staging, reserved against the limits before
  // their bytes arrive, so selections made together cannot pass them.
  private int _stagingCount;
  private long _stagingBytes;

  // The revision the thread's first page was read at; older pages are
  // asked for relative to it.
  private long _threadSeen;

  // The history shown: which one (a new opening or a gap starts another),
  // the cursor below its oldest message and whether older pages remain,
  // an older page on its way or refused, and new messages arrived below
  // while the dispatcher reads further up.
  private int _history;
  private MessageCursor? _below;
  private bool _older,
    _loadingOlder,
    _olderFailed,
    _newBelow;

  // The conversation's scroller and the script that keeps its place.
  private ElementReference _scroller;
  private string? _scrolled;
  private IJSObjectReference? _threadScript;
  private IJSObjectReference? _scrolling;
  private DotNetObjectReference<Messages>? _self;

  private Guid? _shown;
  private ConversationContext? _context;
  private bool _contextFailed;

  // The trip opens over the conversation where there is no room beside it.
  private bool _trip;

  // The signed-in dispatcher's name: their own replies read "You".
  private string? _me;
  private int _contextRead;
  private DateTime _claimedAt = DateTime.MinValue;
  private IJSObjectReference? _files;

  // The conversation pane, where Enter sends and files can be dropped.
  private ElementReference _threadPane;
  private IJSObjectReference? _composer;
  private readonly CancellationTokenSource _lifetime = new();

  private MessageTemplateView? Chosen =>
    DispatcherTemplates.FirstOrDefault(x => x.Name == _template);

  private bool TemplateReady =>
    Chosen is { } chosen
    && Enumerable
      .Range(0, chosen.Parameters)
      .All(i => !string.IsNullOrWhiteSpace(Parameter(i)));

  // Nothing here is awaited before the conversation is read: the stream,
  // the templates and the list each load beside it and show when they land.
  protected override async Task OnInitializedAsync()
  {
    Signals.Changed += OnSignal;
    DriverGroup.Changed += OnDriverGroupChanged;
    Start(Signals.JoinAsync);
    Start(LoadTemplatesAsync);
    Start(RefreshListAsync);
    if (Authentication is not null)
      _me = (await Authentication).User.Identity?.Name;
  }

  // The conversation and its trip are read side by side; the messages show
  // as soon as they arrive, before the trip and before they are marked read.
  protected override void OnParametersSet()
  {
    if (_shown == Id)
      return;
    _shown = Id;
    _editor++;
    _trip = false;
    _thread = null;
    _messages = [];
    _newBelow = false;
    _context = null;
    _contextFailed = false;
    ResetDraft();
    if (Id is { } id)
    {
      Start(() => RefreshThreadAsync(id));
      Start(() => LoadContextAsync(id));
    }
  }

  // A read that runs beside the others: the page shows its answer when it
  // lands, whatever is still on its way. Every read fences its own answer
  // by generation and conversation, so a late one changes nothing.
  private void Start(Func<Task> read) => _ = ShowAsync(read);

  private async Task ShowAsync(Func<Task> read)
  {
    try
    {
      await read();
    }
    catch (Exception ex)
    {
      if (!_disposed)
        await DispatchExceptionAsync(ex);
      return;
    }
    if (!_disposed)
      StateHasChanged();
  }

  private async Task LoadTemplatesAsync()
  {
    var templates = await Api.GetAsync<List<MessageTemplateView>>(
      "api/messaging/templates",
      _lifetime.Token
    );
    if (!_disposed && templates.Success && templates.Response is { } list)
      _templates = list;
  }

  // Loads offered for filing: the driver's current ones, only when they
  // are on one truck.
  private IReadOnlyList<ContextLoad> Suggestions => _context?.Loads ?? [];

  // Read when a conversation is opened or its driver changes, not on every
  // signal: it costs a read of the truck's work.
  private async Task LoadContextAsync(Guid id)
  {
    var generation = ++_contextRead;
    var result = await Api.GetAsync<ConversationContext>(
      $"api/messaging/conversations/{id}/context",
      _lifetime.Token
    );
    if (_disposed || generation != _contextRead || Id != id)
      return;
    _context = result.Success ? result.Response : null;
    _contextFailed = !result.Success;
  }

  // Both answer for the conversation that asked: a change or a filing
  // that completes after the dispatcher moved on does not touch the
  // conversation now open.
  private async Task ContextChangedAsync(Guid conversation)
  {
    if (Id != conversation)
      return;
    await RefreshThreadAsync(conversation);
    await LoadContextAsync(conversation);
  }

  // The driver's conversation, opened or just created, opens at once; the
  // list is read again beside it.
  private void ChatOpened(Guid conversation)
  {
    Navigation.NavigateTo($"/messages/{conversation}");
    Start(RefreshListAsync);
  }

  private async Task FiledAsync(Guid conversation)
  {
    if (Id == conversation)
      await RefreshThreadAsync(conversation);
  }

  // The count is the navigation's; a colleague's tab marking something
  // read changes only this dispatcher's inbox counts, not the thread. A
  // change to the open conversation reads it beside the list. A poll tick
  // names no conversation: the list is read first, and the open one only
  // when the list shows it at another revision or does not show it.
  private void OnSignal(MessagingSignal signal) =>
    _ = InvokeAsync(() =>
    {
      if (_disposed || signal.Kind == "unread")
        return;
      if (signal.Kind == "read" || Id is not { } id)
        Start(RefreshListAsync);
      else if (signal.ConversationId == id || signal.Kind == "resync")
      {
        Start(RefreshListAsync);
        Start(() => RefreshThreadAsync(id));
      }
      else if (signal.ConversationId is not null)
        Start(RefreshListAsync);
      else
        Start(async () =>
        {
          await RefreshListAsync();
          if (_disposed || Id != id || Current(id))
            return;
          StateHasChanged();
          await RefreshThreadAsync(id);
        });
    });

  // Whether the list, as just read, shows the open conversation at the
  // revision its thread was read at.
  private bool Current(Guid id) =>
    _thread?.Summary.Id == id
    && _conversations?.FirstOrDefault(x => x.Id == id) is { } listed
    && listed.Revision == _thread.Summary.Revision;

  private async Task ReloadAsync()
  {
    _error = null;
    await RefreshListAsync();
    if (Id is { } id)
      await RefreshThreadAsync(id);
  }

  private async Task FilterAsync(bool unread)
  {
    _unreadOnly = unread;
    await StartOverAsync();
  }

  private async Task SearchAsync()
  {
    _searched = _search.Trim();
    await StartOverAsync();
  }

  private async Task StartOverAsync()
  {
    _listVersion++;
    _conversations = null;
    _next = null;
    _extended = false;
    _loadingMore = false;
    await LoadInboxAsync();
  }

  private string InboxPath(InboxCursor? after) =>
    $"api/messaging/inbox?unread={(_unreadOnly ? "true" : "false")}"
    + (_searched.Length > 0 ? $"&search={Uri.EscapeDataString(_searched)}" : "")
    + (
      after is null
        ? ""
        : $"&afterAt={Uri.EscapeDataString(after.At.ToString("O"))}"
          + $"&afterId={after.Id}"
    );

  // The first page, read again on every change. Pages the dispatcher
  // already opened below it are kept: the first page replaces what it
  // covers, and the rest stays where it was until they ask for more. A
  // conversation that moved to the top is shown there, not twice.
  private async Task LoadInboxAsync()
  {
    var generation = ++_inboxRead;
    var version = _listVersion;
    var result = await Api.GetAsync<InboxView>(
      InboxPath(null),
      _lifetime.Token
    );
    if (_disposed || generation != _inboxRead || version != _listVersion)
      return;
    if (!result.Success || result.Response is not { } inbox)
    {
      _error = result.ErrorMessage;
      return;
    }
    var first = inbox.Conversations;
    // The rows below the new first page's last one, as already shown; the
    // order of ids is the database's, so it is read from the list rather
    // than compared here. When that row is not in the list, start over.
    var end =
      _extended && _conversations is not null && inbox.Next is { } next
        ? _conversations.FindIndex(x => x.Id == next.Id)
        : -1;
    if (end < 0)
    {
      _conversations = [.. first];
      _next = inbox.Next;
      _extended = false;
      _moreRead++;
      _loadingMore = false;
      return;
    }
    var ids = first.Select(x => x.Id).ToHashSet();
    _conversations =
    [
      .. first,
      .. _conversations![(end + 1)..].Where(x => !ids.Contains(x.Id)),
    ];
  }

  private async Task LoadMoreAsync()
  {
    if (_next is not { } after || _loadingMore)
      return;
    _loadingMore = true;
    var generation = ++_moreRead;
    var version = _listVersion;
    var result = await Api.GetAsync<InboxView>(
      InboxPath(after),
      _lifetime.Token
    );
    if (_disposed || generation != _moreRead || version != _listVersion)
      return;
    _loadingMore = false;
    if (!result.Success || result.Response is not { } page)
    {
      _error = result.ErrorMessage;
      return;
    }
    var shown = (_conversations ?? []).Select(x => x.Id).ToHashSet();
    _conversations =
    [
      .. _conversations ?? [],
      .. page.Conversations.Where(x => !shown.Contains(x.Id)),
    ];
    _next = page.Next;
    _extended = true;
  }

  // The newest page, merged in front of the older pages already shown: it
  // joins them at its own oldest message, so a reread (a signal, a reply,
  // a status) keeps the history and the place the dispatcher scrolled to.
  // A newest page that no longer reaches the history shown - more than a
  // page arrived meanwhile - starts the history over rather than show a
  // gap.
  private async Task LoadThreadAsync(Guid id)
  {
    var generation = ++_threadRead;
    var result = await Api.GetAsync<ConversationView>(
      $"api/messaging/conversations/{id}",
      _lifetime.Token
    );
    if (_disposed || generation != _threadRead || Id != id)
      return;
    if (!result.Success || result.Response is not { } thread)
    {
      _error = result.ErrorMessage;
      return;
    }
    // Newest first from the server; the thread reads oldest first.
    List<MessageView> page = [.. thread.Messages.Reverse()];
    var newest = _messages.LastOrDefault()?.Id;
    var join =
      _thread?.Summary.Id == id && page.Count > 0
        ? _messages.FindIndex(x => x.Id == page[0].Id)
        : -1;
    if (join >= 0)
      _messages = [.. _messages[..join], .. page];
    else
    {
      _history++;
      _threadSeen = thread.Summary.Revision;
      _messages = page;
      _below = thread.Next;
      _older = thread.Older;
      _loadingOlder = false;
      _olderFailed = false;
    }
    _thread = thread with { Older = _older, Next = _below };
    if (
      newest is not null
      && _messages.LastOrDefault()?.Id != newest
      && _scrolling is not null
    )
      _newBelow |= !await IsNearNewestAsync();
    // Shown before the read marker is acknowledged.
    StateHasChanged();
    await MarkReadAsync(id, thread);
  }

  private Task RefreshThreadAsync(Guid id) =>
    _threads.RequestAsync(
      id,
      () => LoadThreadAsync(id),
      () => !_disposed && Id == id
    );

  private Task RefreshListAsync() =>
    _lists.RequestAsync(true, LoadInboxAsync, () => !_disposed);

  // Through what the server says this page lets be read: never past a
  // driver message the dispatcher has not been shown. Nothing unread for
  // this dispatcher means the marker already covers every driver message,
  // so a reread (a poll, a reply) asks nothing.
  private async Task MarkReadAsync(Guid id, ConversationView page)
  {
    if (page.Summary.Unread == 0 || page.Messages.Count == 0)
      return;
    var marked = await Api.PostAsync<ReadRequest, bool>(
      $"api/messaging/conversations/{id}/read",
      new(page.ReadThrough ?? page.Summary.Revision),
      _lifetime.Token
    );
    // Other tabs, and the navigation count, learn of it only when this
    // read changed something.
    if (marked.Success && page.Summary.Unread > 0 && !_disposed)
      await Signals.AnnounceReadAsync();
  }

  // The next older page, below the oldest message shown, by its place in
  // the thread's order: one at a time, when the dispatcher scrolls near
  // the top or asks. It belongs to the history it continues; one started
  // over since (another conversation, or a gap) drops it. Messages already
  // shown are not repeated.
  private async Task OlderAsync()
  {
    if (
      Id is not { } id
      || _thread is null
      || _below is not { } before
      || !_older
      || _loadingOlder
    )
      return;
    var history = _history;
    _loadingOlder = true;
    _olderFailed = false;
    StateHasChanged();
    var result = await Api.GetAsync<ConversationView>(
      $"api/messaging/conversations/{id}"
        + $"?beforeSentAt={Uri.EscapeDataString(before.SentAt.ToString("O"))}"
        + "&beforeCreatedAt="
        + Uri.EscapeDataString(before.CreatedAt.ToString("O"))
        + $"&beforeId={before.Id}&seen={_threadSeen}",
      _lifetime.Token
    );
    if (_disposed || Id != id || history != _history)
      return;
    _loadingOlder = false;
    if (!result.Success || result.Response is not { } page)
    {
      _olderFailed = true;
      return;
    }
    var shown = _messages.Select(x => x.Id).ToHashSet();
    _messages =
    [
      .. page.Messages.Reverse().Where(x => !shown.Contains(x.Id)),
      .. _messages,
    ];
    _below = page.Next;
    _older = page.Older;
    _thread = _thread with { Older = _older, Next = _below };
    StateHasChanged();
    await MarkReadAsync(id, page);
  }

  // The scroller asks for the next older page when it comes near the top.
  [JSInvokable]
  public Task NearOldest() =>
    InvokeAsync(() =>
    {
      if (_older && !_loadingOlder && !_olderFailed)
        Start(OlderAsync);
    });

  // The dispatcher is back at the newest message.
  [JSInvokable]
  public Task AtNewest() =>
    InvokeAsync(() =>
    {
      if (!_newBelow)
        return;
      _newBelow = false;
      StateHasChanged();
    });

  private async Task ToNewestAsync()
  {
    _newBelow = false;
    if (_scrolling is not null)
      try
      {
        await _scrolling.InvokeVoidAsync("toNewest", _lifetime.Token);
      }
      catch (JSException) { }
  }

  private async Task<bool> IsNearNewestAsync()
  {
    try
    {
      return await _scrolling!.InvokeAsync<bool>(
        "isNearNewest",
        _lifetime.Token
      );
    }
    catch (JSException)
    {
      return true;
    }
  }

  private Task SendAsync() => SendAsync(confirm: false);

  private Task SendAnywayAsync() =>
    _templateStale
      ? SendTemplateAsync(confirm: true)
      : SendAsync(confirm: true);

  private bool _templateStale;

  private async Task SendAsync(bool confirm)
  {
    if (Id is not { } id || _busy)
      return;
    if (
      Staged.Where(x => x.State != StagedState.Sending).ToList() is
      { Count: > 0 } files
    )
    {
      await SendFilesAsync(id, files, confirm);
      return;
    }
    if (_draft.Trim().Length == 0)
      return;
    var editor = Begin(id);
    _refusal = null;
    _draftKey ??= Guid.NewGuid();
    var result = await Api.PostAsync<SendMessageRequest, MessageView>(
      $"api/messaging/conversations/{id}/messages",
      new(_draft, _draftKey.Value, _messages.LastOrDefault()?.Id, confirm),
      _lifetime.Token
    );
    if (!Settled(id, editor))
      return;
    if (result.Success)
    {
      ResetDraft();
      await ToNewestAsync();
      await RefreshThreadAsync(id);
      return;
    }
    Refuse(result.ErrorMessage, template: false);
  }

  // PulsR's contact request, when an administrator recorded it for this
  // number exactly as PulsR defines it after Meta approved it.
  // The carrier's own templates; PulsR's (the contact request) have their
  // own button and parameters the server fills.
  private IEnumerable<MessageTemplateView> DispatcherTemplates =>
    _templates.Where(x => x.Purpose is null);

  private MessageTemplateView? ContactTemplate =>
    _templates.FirstOrDefault(x => x.Purpose == "contactRequest");

  // One click sends it once: its retry key lasts until it is queued, so a
  // click again after an answer that never came is the same request.
  private Guid? _contactKey;

  private async Task RequestContactAsync()
  {
    if (Id is not { } id || _busy || ContactTemplate is not { } contact)
      return;
    var editor = Begin(id);
    _refusal = null;
    _contactKey ??= Guid.NewGuid();
    var result = await Api.PostAsync<TemplateRequest, MessageView>(
      $"api/messaging/conversations/{id}/templates",
      new(_contactKey.Value, contact.Name, contact.Language, []),
      _lifetime.Token
    );
    if (!Settled(id, editor))
      return;
    if (!result.Success)
    {
      _refusal = result.ErrorMessage;
      return;
    }
    _contactKey = null;
    await ToNewestAsync();
    await RefreshThreadAsync(id);
  }

  private Task SendTemplateAsync() => SendTemplateAsync(confirm: false);

  private async Task SendTemplateAsync(bool confirm)
  {
    if (Id is not { } id || _busy || Chosen is not { } chosen || !TemplateReady)
      return;
    var editor = Begin(id);
    _refusal = null;
    _draftKey ??= Guid.NewGuid();
    var result = await Api.PostAsync<TemplateRequest, MessageView>(
      $"api/messaging/conversations/{id}/templates",
      new(
        _draftKey.Value,
        chosen.Name,
        chosen.Language,
        [.. Enumerable.Range(0, chosen.Parameters).Select(Parameter)]
      ),
      _lifetime.Token
    );
    if (!Settled(id, editor))
      return;
    if (result.Success)
    {
      ResetDraft();
      await RefreshThreadAsync(id);
      return;
    }
    Refuse(result.ErrorMessage, template: true);
  }

  // Picked or dropped files wait under the reply box, each with its own
  // preview, until Send; each can be taken off before then.
  private async Task StageAsync(InputFileChangeEventArgs args)
  {
    if (Id is not { } id || _busy)
      return;
    var editor = _editor;
    _refusal = null;
    foreach (var file in args.GetMultipleFiles(args.FileCount))
    {
      if (Staged.Count() + _stagingCount >= StagedFile.MaximumStaged)
      {
        _refusal = $"Send at most {StagedFile.MaximumStaged} files at once.";
        break;
      }
      if (!StagedFile.Accepts(file.ContentType))
      {
        _refusal = "PDFs, photos, audio and MP4 video can be sent.";
        continue;
      }
      if (file.Size <= 0 || file.Size > StagedFile.Limit(file.ContentType))
      {
        _refusal = "Photos up to 5 MB and other files up to 16 MB can be sent.";
        continue;
      }
      if (
        _staged.Sum(x => (long)x.Bytes.Length) + _stagingBytes + file.Size
        > StagedFile.MaximumTotal
      )
      {
        _refusal =
          "Files waiting to be sent total at most "
          + $"{StagedFile.MaximumTotal / (1024 * 1024)} MB.";
        break;
      }
      _stagingCount++;
      _stagingBytes += file.Size;
      StagedFile staged;
      try
      {
        staged = await StagedFile.ReadAsync(file, id, _lifetime.Token);
      }
      finally
      {
        _stagingCount--;
        _stagingBytes -= file.Size;
      }
      // Read for a session that has since ended: its reply is gone.
      if (_disposed || Id != id || _editor != editor)
        return;
      _staged.Add(staged);
    }
  }

  private void Unstage(StagedFile file)
  {
    if (file.State != StagedState.Sending)
      _staged.Remove(file);
  }

  private IEnumerable<StagedFile> Staged =>
    _staged.Where(x => x.ConversationId == Id);

  private Task RetryFileAsync(StagedFile file) =>
    Id is { } id && !_busy
      ? SendFilesAsync(id, [file], confirm: false)
      : Task.CompletedTask;

  // One file message each, in order; the reply's text is the first one's
  // caption. A file keeps its retry key and caption, so sending it again
  // is the same message. The answers belong to the conversation they were
  // sent in: another conversation opened meanwhile is left as it is.
  private async Task SendFilesAsync(
    Guid id,
    IReadOnlyList<StagedFile> files,
    bool confirm
  )
  {
    var text = _draft.Trim();
    if (text.Length > StagedFile.MaximumCaption && files[0].Caption is null)
    {
      _refusal =
        $"A caption is at most {StagedFile.MaximumCaption} characters. "
        + "Send the text on its own first.";
      return;
    }
    var editor = Begin(id);
    _refusal = null;
    if (files[0].Caption is null && text.Length > 0)
    {
      files[0].Caption = text;
      _draft = "";
      _draftKey = null;
    }
    var lastSeen = _messages.LastOrDefault()?.Id;
    string? stale = null;
    foreach (var file in files)
    {
      file.State = StagedState.Sending;
      file.Error = null;
      if (Id == id)
        StateHasChanged();
      using var form = file.Form(lastSeen, confirm);
      var result = await Api.PostFormAsync<MessageView>(
        $"api/messaging/conversations/{id}/files",
        form,
        _lifetime.Token
      );
      if (result.Success)
      {
        _staged.Remove(file);
        continue;
      }
      file.State = StagedState.Failed;
      file.Error = result.ErrorMessage;
      if (IsStale(result.ErrorMessage))
      {
        stale = result.ErrorMessage;
        break;
      }
    }
    if (!Settled(id, editor))
      return;
    if (stale is not null)
      Refuse(stale, template: false);
    await RefreshThreadAsync(id);
  }

  private async Task RetryAsync(MessageView message)
  {
    if (Id is not { } id || _busy)
      return;
    var editor = Begin(id);
    var result = await Api.PostAsync<object, MessageView>(
      $"api/messaging/messages/{message.Id}/retry",
      new { },
      _lifetime.Token
    );
    if (!Settled(id, editor))
      return;
    if (!result.Success)
      _refusal = result.ErrorMessage;
    await RefreshThreadAsync(id);
  }

  // Tells colleagues who is answering, at most once a minute.
  private async Task ClaimAsync()
  {
    if (
      Id is not { } id
      || DateTime.UtcNow - _claimedAt < TimeSpan.FromMinutes(1)
    )
      return;
    _claimedAt = DateTime.UtcNow;
    await Api.PostAsync<object, bool>(
      $"api/messaging/conversations/{id}/claim",
      new { },
      _lifetime.Token
    );
  }

  private async Task OpenAsync(AttachmentView attachment)
  {
    var file = await Api.GetFileAsync(
      $"api/messaging/attachments/{attachment.Id}/content",
      _lifetime.Token
    );
    if (_disposed)
      return;
    if (file is not { } found)
    {
      _refusal = "The file could not be opened. Please retry.";
      return;
    }
    try
    {
      _files ??= await JS.InvokeAsync<IJSObjectReference>(
        "import",
        _lifetime.Token,
        "./js/generated/dispatch/documents.js"
      );
      await _files.InvokeVoidAsync(
        "download",
        _lifetime.Token,
        found.Name,
        found.Bytes
      );
    }
    catch (JSException)
    {
      _refusal = "The browser could not open this file. Please retry.";
    }
  }

  private int Begin(Guid id) => _sending[id] = _editor;

  // Ends the send for its conversation. Its answer may touch the draft,
  // files and refusal only in the session that sent it; the conversation
  // opened again since is only read again, to show what was sent.
  private bool Settled(Guid id, int editor)
  {
    _sending.Remove(id);
    if (_disposed || Id != id)
      return false;
    if (_editor == editor)
      return true;
    Start(() => RefreshThreadAsync(id));
    return false;
  }

  private void Refuse(string message, bool template)
  {
    _refusal = message;
    _stale = IsStale(message);
    _templateStale = template && _stale;
  }

  private static bool IsStale(string message) =>
    message.Contains("newer message", StringComparison.Ordinal);

  // Also takes off files staged and not on their way: they belonged to the
  // reply being written.
  private void ResetDraft()
  {
    _staged.RemoveAll(x => x.State != StagedState.Sending);
    _contactKey = null;
    _draft = "";
    _draftKey = null;
    _refusal = null;
    _stale = false;
    _templateStale = false;
    _template = null;
    _parameters.Clear();
  }

  private string Parameter(int index) =>
    _parameters.GetValueOrDefault(index) ?? "";

  private void SetParameter(int index, string? value) =>
    _parameters[index] = value ?? "";

  private const string Accepted =
    ".pdf,image/jpeg,image/png,image/webp,audio/*,video/mp4";

  private string Conversation(ConversationSummary item) =>
    "messages__conversation"
    + (item.Id == Id ? " is-selected" : "")
    + (item.Unread > 0 ? " is-unread" : "");

  private static string StagedClass(StagedFile file) =>
    "messages__staged-file"
    + (file.State == StagedState.Failed ? " is-failed" : "")
    + (file.State == StagedState.Sending ? " is-sending" : "");

  private static string Unread(ConversationSummary item) =>
    $"{item.Unread} unread";

  private bool CanSend =>
    !_busy
    && (
      _draft.Trim().Length > 0
      || Staged.Any(x => x.State != StagedState.Sending)
    );

  private static string ParameterId(int index) => $"messages-parameter-{index}";

  private static string Placeholder(int index) => $"{{{{{index + 1}}}}}";

  private static string Stamp(DateTime at) => at.ToString("O");

  private static string Who(ConversationSummary conversation) =>
    conversation.DriverName ?? conversation.Participant;

  // Another dispatcher is answering; one's own claim is not news.
  private bool Colleague(ConversationSummary conversation) =>
    conversation.ClaimedBy is { } by && by != _me;

  private static string FirstName(ConversationSummary conversation) =>
    conversation.DriverName?.Split(' ')[0] ?? conversation.Participant;

  private static string Initials(ConversationSummary conversation) =>
    conversation.DriverName is { } name
      ? string.Concat(
        name.Split(' ', StringSplitOptions.RemoveEmptyEntries)
          .Take(2)
          .Select(x => char.ToUpperInvariant(x[0]))
      )
      : "#";

  private static string Titled(string status) =>
    status.Length == 0
      ? status
      : char.ToUpperInvariant(status[0]) + status[1..].Replace('_', ' ');

  // How long WhatsApp still takes free-form replies, from the driver's
  // last message; the server decides again when the reply is sent. Only
  // replies are limited: the conversation and its history stay.
  internal static string ReplyWindow(DateTime? lastInbound, DateTime now)
  {
    if (lastInbound is not { } last)
      return "Reply window open";
    var left = last.AddHours(24) - now;
    if (left.TotalHours >= 1)
    {
      var hours = (int)left.TotalHours;
      return $"Reply window: {hours} {(hours == 1 ? "hour" : "hours")} left";
    }
    var minutes = Math.Max(1, (int)Math.Ceiling(left.TotalMinutes));
    return $"Reply window: {minutes} "
      + $"{(minutes == 1 ? "minute" : "minutes")} left";
  }

  private static string FreeFor(ConversationSummary conversation) =>
    ReplyWindow(conversation.LastInboundAt, DateTime.UtcNow);

  // Drive and shift left, for the conversation's header where the trip
  // does not fit beside it.
  private static string? Glance(ContextHours? hours) =>
    hours is { Known: true } known
      ? $"Drive {Clock(known.DriveMs)} · Shift {Clock(known.ShiftMs)} left"
      : null;

  internal static string Clock(long? milliseconds)
  {
    if (milliseconds is null)
      return "—";
    var minutes = Math.Max(0, milliseconds.Value) / 60000;
    return $"{minutes / 60}:{minutes % 60:00}";
  }

  // The messages with the day each one opens, when it is the first of its
  // day in the thread.
  private IEnumerable<(MessageView, string?)> Days()
  {
    DateTime? last = null;
    foreach (var message in _messages)
    {
      var day = message.SentAt.ToLocalTime().Date;
      yield return (message, day == last ? null : DayLabel(day));
      last = day;
    }
  }

  private static string DayLabel(DateTime day) =>
    day == DateTime.Now.Date ? "Today"
    : day == DateTime.Now.Date.AddDays(-1) ? "Yesterday"
    : day.ToString("MMM d", CultureInfo.InvariantCulture);

  internal static string When(DateTime at) =>
    at.ToLocalTime().Date == DateTime.Now.Date
      ? at.ToLocalTime().ToString("HH:mm")
      : at.ToLocalTime().ToString("MMM d, HH:mm");

  public static string Status(string status) =>
    status switch
    {
      "queued" => "Waiting to send",
      "sending" => "Sending",
      "accepted" => "Accepted",
      "sent" => "Sent",
      "delivered" => "Delivered",
      "read" => "Read",
      "failed" => "Not delivered",
      "rejected" => "Refused",
      "unknown" => "No answer from WhatsApp",
      "withdrawn" => "Not sent",
      _ => status,
    };

  protected override async Task OnAfterRenderAsync(bool firstRender)
  {
    await FollowScrollerAsync();
    if (!firstRender)
      return;
    try
    {
      var module = await JS.InvokeAsync<IJSObjectReference>(
        "import",
        _lifetime.Token,
        "./js/generated/messages/composer.js"
      );
      if (module is null || _disposed)
        return;
      _composer = await module.InvokeAsync<IJSObjectReference>(
        "attach",
        _lifetime.Token,
        _threadPane
      );
    }
    catch (JSException)
    {
      // Without it the Send button and the paperclip still work.
    }
  }

  // Each conversation's scroller is a new element: the script follows it.
  private async Task FollowScrollerAsync()
  {
    var shown = _thread is null ? null : _scroller.Id;
    if (shown == _scrolled || _disposed)
      return;
    _scrolled = shown;
    try
    {
      if (_scrolling is not null)
      {
        await _scrolling.InvokeVoidAsync("dispose");
        await _scrolling.DisposeAsync();
        _scrolling = null;
      }
      if (shown is null)
        return;
      _threadScript ??= await JS.InvokeAsync<IJSObjectReference>(
        "import",
        _lifetime.Token,
        "./js/generated/messages/thread.js"
      );
      if (_threadScript is null)
        return;
      _self ??= DotNetObjectReference.Create(this);
      _scrolling = await _threadScript.InvokeAsync<IJSObjectReference>(
        "attach",
        _lifetime.Token,
        _scroller,
        _self
      );
    }
    catch (JSException)
    {
      // Without it the history still loads with "Show earlier messages".
    }
  }

  // The dispatcher chose another driver group: the list starts over.
  private void OnDriverGroupChanged() =>
    _ = InvokeAsync(async () =>
    {
      if (_disposed)
        return;
      await StartOverAsync();
      StateHasChanged();
    });

  public async ValueTask DisposeAsync()
  {
    _disposed = true;
    Signals.Changed -= OnSignal;
    DriverGroup.Changed -= OnDriverGroupChanged;
    await Signals.LeaveAsync();
    _lifetime.Cancel();
    _lifetime.Dispose();
    if (_files is not null)
      try
      {
        await _files.DisposeAsync();
      }
      catch (JSDisconnectedException) { }
    if (_composer is not null)
      try
      {
        await _composer.InvokeVoidAsync("dispose");
        await _composer.DisposeAsync();
      }
      catch (JSDisconnectedException) { }
    if (_scrolling is not null)
      try
      {
        await _scrolling.InvokeVoidAsync("dispose");
        await _scrolling.DisposeAsync();
      }
      catch (JSDisconnectedException) { }
    _self?.Dispose();
  }
}
