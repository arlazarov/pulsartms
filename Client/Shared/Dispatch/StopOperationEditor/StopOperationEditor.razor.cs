using System.Net;
using Client.Models.DTO.Dispatch;
using Client.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;

namespace Client.Shared.Dispatch.StopOperationEditor;

public partial class StopOperationEditor
{
  [Parameter]
  public string ButtonLabel { get; set; } = "Edit stop operation";

  [Parameter]
  public bool Inline { get; set; }

  [Parameter]
  public bool PageActions { get; set; }
  public bool IsSaving => _saving;
  public bool CanSubmit => _editing && IsDirty && !_saving && !_disposed;

  public async Task SubmitAsync()
  {
    if (CanSubmit)
      await SaveAsync(false);
    if (!_disposed)
      StateHasChanged();
  }

  public async Task DiscardAsync()
  {
    if (_saving || _disposed)
      return;
    await Cancel();
    StateHasChanged();
  }

  [Parameter]
  public EventCallback<bool> DraftChanged { get; set; }

  [Inject]
  private ApiService Api { get; set; } = default!;

  [Inject]
  private IJSRuntime JS { get; set; } = default!;

  [Parameter]
  public Guid DispatchId { get; set; }

  [Parameter, EditorRequired]
  public DispatchStopResponse Stop { get; set; } = default!;

  [Parameter]
  public EventCallback Changed { get; set; }
  private static readonly string[] Actions =
  [
    "Driver start",
    "Collect truck",
    "Collect trailer",
    "Pick Up",
    "Drop Off",
    "Drop trailer",
    "Waypoint",
  ];
  private readonly CancellationTokenSource _lifetime = new();
  private bool _editing,
    _saving,
    _disposed;
  private Guid _dispatchId,
    _stopId;
  private long _revision;
  private string _identity = "",
    _action = "Waypoint",
    _state = "Unknown";
  private string? _error;
  private string _sourceJob = "",
    _sourceState = "";
  private string _initialAction = "",
    _initialState = "";
  private bool IsDirty => _action != _initialAction || _state != _initialState;
  private string ActionId => $"stop-operation-{DispatchId}-{Stop.Id}-action";
  private string[] States =>
    _action switch
    {
      "Driver start" => ["No truck"],
      "Pick Up" => ["Loaded"],
      "Drop trailer" => ["Bobtail"],
      "Collect trailer" or "Drop Off" => ["Unknown", "Empty", "Loaded"],
      _ => ["Unknown", "Bobtail", "Empty", "Loaded"],
    };

  protected override void OnParametersSet()
  {
    if (_editing && (_dispatchId != DispatchId || _stopId != Stop.Id))
      _editing = false;
    if (
      Inline
      && (
        !_editing
        || (
          !IsDirty
          && (
            _revision != Stop.OperationRevision
            || _identity != Stop.CompletionIdentity
            || _sourceJob != Stop.Job
            || _sourceState != Stop.StateAfter
          )
        )
      )
    )
      Begin();
  }

  private void Begin()
  {
    _dispatchId = DispatchId;
    _stopId = Stop.Id;
    _revision = Stop.OperationRevision;
    _identity = Stop.CompletionIdentity;
    _sourceJob = Stop.Job;
    _sourceState = Stop.StateAfter;
    var action = Stop.ManualAction ?? Stop.Job;
    _action = action switch
    {
      "Delivery" => "Drop Off",
      "Pickup" => "Pick Up",
      _ => Actions.Contains(action) ? action : "Waypoint",
    };
    var state = Stop.ManualStateAfter ?? Stop.StateAfter;
    _state = States.Contains(state) ? state : States[0];
    _initialAction = _action;
    _initialState = _state;
    _error = null;
    _editing = true;
  }

  private async Task ActionChanged(ChangeEventArgs args)
  {
    _action = args.Value?.ToString() ?? "Waypoint";
    _state = States[0];
    await DraftChanged.InvokeAsync(IsDirty);
  }

  private async Task Cancel()
  {
    _editing = false;
    _error = null;
    if (Inline)
      Begin();
    await DraftChanged.InvokeAsync(false);
  }

  private async Task SaveAsync(bool reset)
  {
    if (!_editing || _saving)
      return;
    var dispatchId = _dispatchId;
    var stopId = _stopId;
    _saving = true;
    _error = null;
    try
    {
      await DraftChanged.InvokeAsync(IsDirty);
      var result = await Api.PutAsync<StopOperationUpdate, StopOperationState>(
        $"api/dispatch/{dispatchId}/stops/{stopId}/operation",
        new(
          reset ? null : _action,
          reset ? null : _state,
          _revision,
          _identity
        ),
        _lifetime.Token
      );
      if (_disposed || dispatchId != DispatchId || stopId != Stop.Id)
        return;
      if (!result.Success || result.Response is not { } state)
      {
        await JS.InvokeVoidAsync(
          "console.error",
          "Stop operation failed",
          dispatchId,
          stopId,
          result.HttpStatusCode
        );
        _error =
          result.HttpStatusCode == HttpStatusCode.Conflict
            ? "Reload the load and review its truck starting stop and assignments."
            : "Could not save the operation. Check your access and try again.";
        return;
      }
      Stop.ManualAction = state.Action;
      Stop.ManualStateAfter = state.StateAfter;
      Stop.OperationRevision = state.Revision;
      Stop.OperationRecordedAt = state.RecordedAt;
      _editing = false;
      if (Inline)
        Begin();
      await DraftChanged.InvokeAsync(false);
      await Changed.InvokeAsync();
    }
    finally
    {
      _saving = false;
      if (!_disposed)
        await DraftChanged.InvokeAsync(IsDirty);
    }
  }

  public void Dispose()
  {
    _disposed = true;
    _lifetime.Cancel();
    _lifetime.Dispose();
  }
}
