using Client.Models.DTO.Messaging;
using Client.Services;
using Microsoft.AspNetCore.Components;

namespace Client.Pages.Messages;

// One message to several drivers. The dispatcher chooses who (all drivers
// or their driver group, then leaves out anyone they like) and what (a
// text, or an approved template), sees who can be sent it and why not,
// and sends: each driver gets their own message in their own chat. The
// retry key lasts until the broadcast is made, so Send twice is one
// broadcast. Afterwards each driver's message shows its own status, and
// what has not gone yet can be cancelled.
public partial class BroadcastDialog : IDisposable
{
  [Parameter]
  public bool Open { get; set; }

  [Parameter]
  public EventCallback OnClose { get; set; }

  [Parameter]
  public IReadOnlyList<MessageTemplateView> Templates { get; set; } = [];

  [Inject]
  private ApiService Api { get; set; } = default!;

  private string _scope = "all",
    _text = "",
    _template = "";
  private readonly Dictionary<int, string> _parameters = [];
  private BroadcastPreview? _preview;
  private readonly HashSet<Guid> _chosen = [];
  private BroadcastView? _sent;
  private Guid? _key;
  private bool _busy,
    _disposed;
  private string? _error;
  private readonly CancellationTokenSource _lifetime = new();

  private MessageTemplateView? Chosen =>
    Templates.FirstOrDefault(x => x.Name == _template);

  // A template the server fills (the contact request) asks nothing here.
  private int Parameters => Chosen is { Purpose: null } t ? t.Parameters : 0;

  private BroadcastRequest Request(bool forSending)
  {
    var eligible = _preview?.Recipients.Where(x => x.Eligible).ToList() ?? [];
    var narrowed =
      forSending && eligible.Any(x => !_chosen.Contains(x.DriverId));
    return new(
      narrowed ? "selected" : _scope,
      narrowed ? [.. _chosen] : null,
      Chosen is null ? _text : null,
      Chosen?.Name,
      Chosen?.Language,
      Chosen is null
        ? null
        :
        [
          .. Enumerable
            .Range(0, Parameters)
            .Select(i => _parameters.GetValueOrDefault(i) ?? ""),
        ]
    );
  }

  private async Task PreviewAsync()
  {
    _busy = true;
    _error = null;
    var result = await Api.PostAsync<BroadcastRequest, BroadcastPreview>(
      "api/messaging/broadcasts/preview",
      Request(forSending: false),
      _lifetime.Token
    );
    _busy = false;
    if (_disposed)
      return;
    if (!result.Success || result.Response is not { } preview)
    {
      _error = result.ErrorMessage;
      return;
    }
    _preview = preview;
    _chosen.Clear();
    foreach (var recipient in preview.Recipients.Where(x => x.Eligible))
      _chosen.Add(recipient.DriverId);
    _key = null;
  }

  private void Toggle(Guid driver, bool chosen)
  {
    if (chosen)
      _chosen.Add(driver);
    else
      _chosen.Remove(driver);
  }

  private async Task SendAsync()
  {
    if (_preview is null || _chosen.Count == 0 || _busy)
      return;
    _busy = true;
    _error = null;
    _key ??= Guid.NewGuid();
    var result = await Api.PostAsync<BroadcastBody, BroadcastView>(
      "api/messaging/broadcasts",
      new(_key.Value, Request(forSending: true)),
      _lifetime.Token
    );
    _busy = false;
    if (_disposed)
      return;
    if (!result.Success || result.Response is not { } sent)
    {
      _error = result.ErrorMessage;
      return;
    }
    _sent = sent;
  }

  private async Task RefreshAsync()
  {
    if (_sent is not { } sent)
      return;
    var result = await Api.GetAsync<BroadcastView>(
      $"api/messaging/broadcasts/{sent.Id}",
      _lifetime.Token
    );
    if (!_disposed && result.Success && result.Response is { } view)
      _sent = view;
  }

  private async Task CancelAsync()
  {
    if (_sent is not { } sent || _busy)
      return;
    _busy = true;
    var result = await Api.PostAsync<object, BroadcastView>(
      $"api/messaging/broadcasts/{sent.Id}/cancel",
      new { },
      _lifetime.Token
    );
    _busy = false;
    if (_disposed)
      return;
    if (result.Success && result.Response is { } view)
      _sent = view;
    else
      _error = result.ErrorMessage;
  }

  private async Task CloseAsync()
  {
    _preview = null;
    _sent = null;
    _key = null;
    _error = null;
    await OnClose.InvokeAsync();
  }

  private void Scope(string scope)
  {
    _scope = scope;
    Changed();
  }

  private void SetParameter(int index, string? value)
  {
    _parameters[index] = value ?? "";
    Changed();
  }

  private static string TemplateName(MessageTemplateView template) =>
    template.Purpose == "contactRequest" ? "Request contact" : template.Name;

  private void Changed()
  {
    _preview = null;
    _key = null;
  }

  private static string StatusText(BroadcastRecipientView recipient) =>
    recipient.Status switch
    {
      "skipped" => recipient.Reason ?? "Not sent",
      "withdrawn" => "Cancelled before it went",
      "sending" => "Being sent: it may arrive",
      _ => Messages.Status(recipient.Status),
    };

  public void Dispose()
  {
    _disposed = true;
    _lifetime.Cancel();
    _lifetime.Dispose();
  }
}
