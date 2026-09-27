using Client.Models.DTO.Dispatch;
using Client.Models.DTO.Messaging;
using Client.Services;
using Microsoft.AspNetCore.Components;

namespace Client.Pages.Messages;

// Files one of a driver's files to a load, only when the dispatcher
// presses File: the driver's current loads are offered first, any other
// load by its number. The server checks the file and the load again and
// files it once.
public partial class FileToLoad : IDisposable
{
  [Parameter, EditorRequired]
  public AttachmentView Attachment { get; set; } = default!;

  [Parameter]
  public IReadOnlyList<ContextLoad> Loads { get; set; } = [];

  [Parameter]
  public EventCallback OnFiled { get; set; }

  // Already filed somewhere: filing again is offered quietly.
  [Parameter]
  public bool Filed { get; set; }

  [Inject]
  private ApiService Api { get; set; } = default!;

  private bool _open,
    _busy;
  private bool _choiceIsDefault;

  // The select's value: a choice made there is the dispatcher's own.
  private string Choice
  {
    get => _choice;
    set
    {
      _choice = value;
      _choiceIsDefault = false;
    }
  }

  private string _choice = "",
    _kind = "bol";
  private int? _number;
  private string? _error;

  // Keyed by the attachment: a choice made for one file never carries to
  // another. A filing still running when this goes runs to its end - the
  // server files once either way - but its answer is not applied.
  private readonly CancellationTokenSource _lifetime = new();

  protected override void OnParametersSet()
  {
    // A default follows the server's current load as it changes, and goes
    // when there is none; a load the dispatcher chose stays theirs.
    if (_choice.Length == 0 || _choiceIsDefault)
    {
      _choice = Current?.Id.ToString() ?? "";
      _choiceIsDefault = _choice.Length > 0;
    }
  }

  // Only the load the server placed as current is offered by default; a
  // stale, unknown or conflicting place needs the dispatcher's choice.
  private ContextLoad? Current =>
    Loads.FirstOrDefault(x => x.Phase == "current" && x.Conflict is null);

  private bool Ready => !_busy && (_choice.Length > 0 || _number is > 0);

  private string Id(string part) => $"filing-{part}-{Attachment.Id:N}";

  private async Task FileAsync()
  {
    if (!Ready)
      return;
    _busy = true;
    _error = null;
    var request = Guid.TryParse(_choice, out var load)
      ? new FileRequest(load, null, _kind)
      : new FileRequest(null, _number, _kind);
    var result = await Api.PostAsync<FileRequest, DispatchDocumentInfo>(
      $"api/messaging/attachments/{Attachment.Id}/file",
      request
    );
    if (_lifetime.IsCancellationRequested)
      return;
    _busy = false;
    if (!result.Success)
    {
      _error = result.ErrorMessage;
      return;
    }
    _open = false;
    await OnFiled.InvokeAsync();
  }

  public void Dispose()
  {
    _lifetime.Cancel();
    _lifetime.Dispose();
  }

  private void Close()
  {
    _open = false;
    _error = null;
  }
}
