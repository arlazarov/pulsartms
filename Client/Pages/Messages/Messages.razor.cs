using System.Security.Cryptography;
using Client.Models.DTO.Messaging;
using Client.Services;
using Microsoft.AspNetCore.Components;
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
  public const long MaximumFile = 16 * 1024 * 1024;

  [Parameter]
  public Guid? Id { get; set; }

  [Inject]
  private ApiService Api { get; set; } = default!;

  [Inject]
  private MessagingSignals Signals { get; set; } = default!;

  [Inject]
  private IJSRuntime JS { get; set; } = default!;

  private InboxView? _inbox;
  private ConversationView? _thread;
  private List<MessageView> _messages = [];
  private IReadOnlyList<MessageTemplateView> _templates = [];
  private bool _unreadOnly,
    _busy,
    _stale,
    _disposed;
  private string _draft = "";
  private Guid? _draftKey;
  private string? _error,
    _refusal,
    _template;
  private readonly Dictionary<int, string> _parameters = [];
  private int _inboxRead,
    _threadRead;
  private Guid? _shown;
  private DateTime _claimedAt = DateTime.MinValue;
  private IJSObjectReference? _files;
  private readonly CancellationTokenSource _lifetime = new();

  private MessageTemplateView? Chosen =>
    _templates.FirstOrDefault(x => x.Name == _template);

  private bool TemplateReady =>
    Chosen is { } chosen
    && Enumerable
      .Range(0, chosen.Parameters)
      .All(i => !string.IsNullOrWhiteSpace(Parameter(i)));

  protected override async Task OnInitializedAsync()
  {
    Signals.Changed += OnSignal;
    await Signals.JoinAsync();
    var templates = await Api.GetAsync<List<MessageTemplateView>>(
      "api/messaging/templates",
      _lifetime.Token
    );
    if (templates.Success && templates.Response is { } list)
      _templates = list;
    await LoadInboxAsync();
  }

  protected override async Task OnParametersSetAsync()
  {
    if (_shown == Id)
      return;
    _shown = Id;
    _thread = null;
    _messages = [];
    ResetDraft();
    if (Id is { } id)
      await LoadThreadAsync(id);
  }

  private void OnSignal(MessagingSignal signal) =>
    _ = InvokeAsync(async () =>
    {
      if (_disposed)
        return;
      await LoadInboxAsync();
      if (
        Id is { } id
        && (signal.ConversationId == id || signal.ConversationId is null)
      )
        await LoadThreadAsync(id);
      StateHasChanged();
    });

  private async Task ReloadAsync()
  {
    _error = null;
    await LoadInboxAsync();
    if (Id is { } id)
      await LoadThreadAsync(id);
  }

  private async Task FilterAsync(bool unread)
  {
    _unreadOnly = unread;
    await LoadInboxAsync();
  }

  private async Task LoadInboxAsync()
  {
    var generation = ++_inboxRead;
    var result = await Api.GetAsync<InboxView>(
      $"api/messaging/inbox?unread={(_unreadOnly ? "true" : "false")}",
      _lifetime.Token
    );
    if (_disposed || generation != _inboxRead)
      return;
    if (result.Success && result.Response is { } inbox)
      _inbox = inbox;
    else
      _error = result.ErrorMessage;
  }

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
    _thread = thread;
    // Newest first from the server; the thread reads oldest first.
    _messages = [.. thread.Messages.Reverse()];
    if (_messages.LastOrDefault() is { } newest)
      await Api.PostAsync<ReadRequest, bool>(
        $"api/messaging/conversations/{id}/read",
        new(newest.SentAt),
        _lifetime.Token
      );
  }

  private async Task OlderAsync()
  {
    if (Id is not { } id || _messages.FirstOrDefault() is not { } oldest)
      return;
    var before = Uri.EscapeDataString(oldest.SentAt.ToString("O"));
    var result = await Api.GetAsync<ConversationView>(
      $"api/messaging/conversations/{id}?before={before}",
      _lifetime.Token
    );
    if (_disposed || Id != id || result.Response is not { } page)
      return;
    _messages = [.. page.Messages.Reverse(), .. _messages];
    _thread = _thread! with { Older = page.Older };
  }

  private Task SendAsync() => SendAsync(confirm: false);

  private Task SendAnywayAsync() =>
    _templateStale
      ? SendTemplateAsync(confirm: true)
      : SendAsync(confirm: true);

  private bool _templateStale;

  private async Task SendAsync(bool confirm)
  {
    if (Id is not { } id || _busy || _draft.Trim().Length == 0)
      return;
    _busy = true;
    _refusal = null;
    _draftKey ??= Guid.NewGuid();
    var result = await Api.PostAsync<SendMessageRequest, MessageView>(
      $"api/messaging/conversations/{id}/messages",
      new(_draft, _draftKey.Value, _messages.LastOrDefault()?.Id, confirm),
      _lifetime.Token
    );
    _busy = false;
    if (_disposed)
      return;
    if (result.Success)
    {
      ResetDraft();
      await LoadThreadAsync(id);
      return;
    }
    Refuse(result.ErrorMessage, template: false);
  }

  private Task SendTemplateAsync() => SendTemplateAsync(confirm: false);

  private async Task SendTemplateAsync(bool confirm)
  {
    if (Id is not { } id || _busy || Chosen is not { } chosen || !TemplateReady)
      return;
    _busy = true;
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
    _busy = false;
    if (_disposed)
      return;
    if (result.Success)
    {
      ResetDraft();
      await LoadThreadAsync(id);
      return;
    }
    Refuse(result.ErrorMessage, template: true);
  }

  private async Task AttachAsync(InputFileChangeEventArgs args)
  {
    if (Id is not { } id || _busy)
      return;
    var file = args.File;
    if (file.Size is 0 or > MaximumFile)
    {
      _refusal = "Files up to 16 MB can be sent.";
      return;
    }
    _busy = true;
    _refusal = null;
    byte[] bytes;
    await using (var stream = file.OpenReadStream(MaximumFile, _lifetime.Token))
    {
      using var copy = new MemoryStream((int)file.Size);
      await stream.CopyToAsync(copy, _lifetime.Token);
      bytes = copy.ToArray();
    }
    var content = new ByteArrayContent(bytes);
    content.Headers.ContentType = new(file.ContentType);
    using var form = new MultipartFormDataContent
    {
      { content, "file", file.Name },
      {
        new StringContent(Convert.ToHexStringLower(SHA256.HashData(bytes))),
        "sha256"
      },
      { new StringContent(Guid.NewGuid().ToString()), "idempotencyKey" },
      { new StringContent(_draft.Trim()), "caption" },
      { new StringContent("true"), "confirm" },
    };
    var result = await Api.PostFormAsync<MessageView>(
      $"api/messaging/conversations/{id}/files",
      form,
      _lifetime.Token
    );
    _busy = false;
    if (_disposed)
      return;
    if (result.Success)
    {
      ResetDraft();
      await LoadThreadAsync(id);
    }
    else
      _refusal = result.ErrorMessage;
  }

  private async Task RetryAsync(MessageView message)
  {
    if (Id is not { } id || _busy)
      return;
    _busy = true;
    var result = await Api.PostAsync<object, MessageView>(
      $"api/messaging/messages/{message.Id}/retry",
      new { },
      _lifetime.Token
    );
    _busy = false;
    if (_disposed)
      return;
    if (!result.Success)
      _refusal = result.ErrorMessage;
    await LoadThreadAsync(id);
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

  private void Refuse(string message, bool template)
  {
    _refusal = message;
    _stale = message.Contains("newer message", StringComparison.Ordinal);
    _templateStale = template && _stale;
  }

  private void ResetDraft()
  {
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

  private string Filter(bool unread) =>
    unread == _unreadOnly ? "btn btn--small btn--primary" : "btn btn--small";

  private string Conversation(ConversationSummary item) =>
    "messages__conversation"
    + (item.Id == Id ? " is-selected" : "")
    + (item.Unread > 0 ? " is-unread" : "");

  private static string Unread(ConversationSummary item) =>
    $"{item.Unread} unread";

  private static string Window(ConversationSummary item) =>
    item.WindowOpen ? "messages__window is-open" : "messages__window";

  private bool CanSend => !_busy && _draft.Trim().Length > 0;

  private static string ParameterId(int index) => $"messages-parameter-{index}";

  private static string Placeholder(int index) => $"{{{{{index + 1}}}}}";

  private static string Stamp(DateTime at) => at.ToString("O");

  private static string Who(ConversationSummary conversation) =>
    conversation.DriverName ?? conversation.Participant;

  internal static string When(DateTime at) =>
    at.ToLocalTime().Date == DateTime.Now.Date
      ? at.ToLocalTime().ToString("HH:mm")
      : at.ToLocalTime().ToString("MMM d, HH:mm");

  public static string Status(string status) =>
    status switch
    {
      "queued" => "Waiting to send",
      "sending" => "Sending",
      "accepted" => "Sent to WhatsApp",
      "sent" => "Sent",
      "delivered" => "Delivered",
      "read" => "Read",
      "failed" => "Not delivered",
      "rejected" => "Refused",
      "unknown" => "Not confirmed",
      "withdrawn" => "Not sent",
      _ => status,
    };

  public async ValueTask DisposeAsync()
  {
    _disposed = true;
    Signals.Changed -= OnSignal;
    await Signals.LeaveAsync();
    _lifetime.Cancel();
    _lifetime.Dispose();
    if (_files is not null)
      try
      {
        await _files.DisposeAsync();
      }
      catch (JSDisconnectedException) { }
  }
}
