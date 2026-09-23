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

  [Inject]
  private ApiService Api { get; set; } = default!;

  private bool _open,
    _busy;
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
    if (_choice.Length == 0 && Loads.Count > 0 && !_open)
      _choice = Loads[0].Id.ToString();
  }

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
