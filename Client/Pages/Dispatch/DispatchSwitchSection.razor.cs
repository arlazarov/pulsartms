using Client.Models.DTO.Execution;
using Client.Services;
using Microsoft.AspNetCore.Components;

namespace Client.Pages.Dispatch;

public partial class DispatchSwitchSection : IDisposable
{
  [Inject]
  private ApiService Api { get; set; } = default!;

  [Parameter]
  public Guid DispatchId { get; set; }

  [Parameter]
  public EventCallback Changed { get; set; }

  [Parameter]
  public bool Contextual { get; set; }

  [Parameter]
  public EventCallback<bool> DraftChanged { get; set; }

  private CancellationTokenSource _identity = new();
  private SwitchWorkspace? _workspace;
  private readonly Dictionary<Guid, SwitchDetails> _operations = new();
  private Guid _loadId;
  private Guid? _openOperation;
  private Guid? _pendingRefresh;
  private bool _expanded;
  private bool _planning;
  private bool _reviewing;
  private bool _reading;
  private bool _writing;
  private bool _disposed;
  private Guid? _anchorId;
  private string? _transferKind;
  private bool _focus;
  private ElementReference _heading;
  private string? _error;
  private string? _notice;
  private bool Editing => _planning || _reviewing || _openOperation.HasValue;
  private bool Busy => _reading || _writing;
  private string Id => $"dispatch-switch-{DispatchId}";
  private IEnumerable<SwitchDetails> VisibleOperations =>
    Contextual && _anchorId is { } id
      ? _operations.Values.Where(operation =>
        operation.Participants.Any(row =>
          row.ReleaseVisitId == id || row.ReceiveVisitId == id
        )
      )
      : _operations.Values;

  public async Task OpenAsync(Guid stopId, string? kind = null)
  {
    if (_disposed || Busy || Editing)
      return;
    _expanded = true;
    _anchorId = stopId;
    _transferKind = kind;
    var owner = _identity;
    await ReloadAsync();
    if (_disposed || !ReferenceEquals(owner, _identity))
      return;
    if (kind is "drop_hook" or "resource_handoff" && _error is null)
    {
      if (
        _workspace
          ?.Loads.SingleOrDefault()
          ?.Visits.Any(visit => visit.Id == stopId) != true
      )
        _error = "This stop is not in the current assignment. Reload the load.";
      else
      {
        _planning = true;
        await DraftChanged.InvokeAsync(true);
      }
    }
    _focus = true;
    await InvokeAsync(StateHasChanged);
  }

  private Task RetryAsync() =>
    Contextual && _anchorId is { } id
      ? OpenAsync(id, _transferKind)
      : ReloadAsync();

  protected override async Task OnAfterRenderAsync(bool firstRender)
  {
    if (!_focus || !_expanded || _disposed)
      return;
    _focus = false;
    await _heading.FocusAsync();
  }

  protected override void OnParametersSet()
  {
    if (_loadId == DispatchId)
      return;
    _identity.Cancel();
    _identity.Dispose();
    _identity = new();
    _loadId = DispatchId;
    _workspace = null;
    _operations.Clear();
    _openOperation = null;
    _pendingRefresh = null;
    _expanded = false;
    _planning = false;
    _reviewing = false;
    _reading = false;
    _writing = false;
    _error = null;
    _notice = null;
    _anchorId = null;
    _transferKind = null;
    _focus = false;
  }

  private async Task ToggleAsync()
  {
    if (Busy || Editing)
      return;
    _expanded = !_expanded;
    if (_expanded && _workspace is null)
      await ReloadAsync();
  }

  private async Task ReloadAsync()
  {
    if (_disposed || Busy || Editing)
      return;
    if (_pendingRefresh is { } pending)
    {
      await RefreshOperationAsync(pending);
      return;
    }
    var owner = _identity;
    _reading = true;
    _error = null;
    var result = await Api.GetAsync<SwitchWorkspace>(
      $"api/dispatch/{DispatchId}/execution/switch-workspace",
      owner.Token
    );
    if (_disposed || owner != _identity || owner.IsCancellationRequested)
      return;
    _reading = false;
    if (!result.Success || result.Response is null)
    {
      _error = result.ErrorMessage;
      return;
    }
    if (
      result.Response.Loads.Count != 1
      || result.Response.Loads[0].DispatchId != DispatchId
    )
    {
      _error = "The Switch workspace does not match this load. Refresh it.";
      return;
    }
    _workspace = result.Response;
    _operations.Clear();
    foreach (var operation in _workspace.Operations)
      _operations[operation.Id] = operation;
  }

  private async Task BeginPlanAsync()
  {
    if (Busy || Editing || _workspace is null || _pendingRefresh.HasValue)
      return;
    var owner = _identity;
    await ReloadAsync();
    if (
      _disposed
      || owner != _identity
      || owner.IsCancellationRequested
      || _error is not null
      || _workspace is null
    )
      return;
    _planning = true;
    _notice = null;
    _error = null;
    await DraftChanged.InvokeAsync(true);
  }

  private async Task CancelPlan()
  {
    if (!_writing)
    {
      _planning = false;
      if (Contextual)
        _expanded = false;
      await DraftChanged.InvokeAsync(false);
    }
  }

  private void SetBusy(bool value) => _writing = value;

  private async Task SetEditor(Guid? id)
  {
    _openOperation = id;
    await DraftChanged.InvokeAsync(Editing);
  }

  private async Task SetReview(bool value)
  {
    _reviewing = value;
    await DraftChanged.InvokeAsync(Editing);
  }

  private bool HasOperationReview(SwitchLoadOption load) =>
    _operations.Values.Any(operation =>
      operation.Participants.Any(row =>
        row.DispatchId == load.DispatchId
        && row.SourceReviewExecutionLegId.HasValue
        && row.SourceReviewReason is not null
      )
    );

  private async Task SourceAcceptedAsync(ExecutionSourceApplyResult result)
  {
    if (_disposed)
      return;
    _reviewing = false;
    _openOperation = null;
    await DraftChanged.InvokeAsync(false);
    _notice = "Source changes accepted. Recorded events remain unchanged.";
    var owner = _identity;
    await ReloadAsync();
    if (_disposed || owner != _identity || owner.IsCancellationRequested)
      return;
    if (_error is not null)
      _error =
        "The change was accepted, but current details could not be "
        + "loaded. Refresh Switch; do not submit the acceptance again.";
    await Changed.InvokeAsync();
  }

  private async Task SavedAsync(SwitchResult result)
  {
    if (_disposed)
      return;
    _planning = false;
    _openOperation = null;
    await DraftChanged.InvokeAsync(false);
    _pendingRefresh = result.Id;
    _notice = "Switch saved. Actual events remain independent.";
    _operations.Remove(result.Id);
    var owner = _identity;
    await RefreshOperationAsync(result.Id);
    if (!_disposed && owner == _identity && !owner.IsCancellationRequested)
      await Changed.InvokeAsync();
  }

  private async Task RefreshOperationAsync(Guid id)
  {
    var owner = _identity;
    _reading = true;
    _error = null;
    var result = await Api.GetAsync<SwitchDetails>(
      $"api/execution/switches/{id}",
      owner.Token
    );
    if (_disposed || owner != _identity || owner.IsCancellationRequested)
      return;
    _reading = false;
    if (!result.Success || result.Response is null || result.Response.Id != id)
    {
      _error =
        "The change was saved, but its current details could not be "
        + "loaded. Refresh the Switch; do not submit the change again.";
      return;
    }
    _operations[id] = result.Response;
    _pendingRefresh = null;
  }

  public void Dispose()
  {
    _disposed = true;
    _identity.Cancel();
    _identity.Dispose();
  }
}
