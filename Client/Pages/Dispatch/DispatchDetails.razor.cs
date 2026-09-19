using System.Net;
using System.Text.Json;
using Client.Models.DTO;
using Client.Models.DTO.Dispatch;
using Client.Models.DTO.Dispatch.Workspace;
using Client.Services;
using Client.Shared.Dispatch;
using Client.Shared.Dispatch.StopOperationEditor;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Routing;

namespace Client.Pages.Dispatch;

public partial class DispatchDetails : IDisposable
{
  [Parameter]
  public Guid Id { get; set; }

  [SupplyParameterFromQuery(Name = "stopId")]
  public Guid? StopId { get; set; }

  [Inject]
  private ApiService Api { get; set; } = default!;

  [Inject]
  private PlanningDisplayCache PlanningCache { get; set; } = default!;

  [Inject]
  private NavigationManager Navigation { get; set; } = default!;

  [CascadingParameter]
  public DispatchSettingsState? DisplaySettings { get; set; }

  private static readonly string[] Tabs =
  [
    "Broker & billing",
    "Overview",
    "History",
  ];
  private IEnumerable<string> VisibleTabs =>
    _workspace?.SourceAssignment is null ? Tabs : Tabs.Append("Assignments");
  private DispatchWorkspaceResponse? _workspace;
  private DispatchWorkspaceResponse? _baseline;
  private UpdateDispatchWorkspaceRequest? _pendingSave;
  private DispatchStopWorkspace? _stopEditor;
  private DispatchSwitchSection? _switchEditor;
  private StopOperationEditor? _operationEditor;
  private DispatchStopCorrection? _correctionEditor;
  private DispatchResourceOptions _resourceOptions = new();
  private readonly DispatchStopDrafts _stopDrafts = new();
  private CancellationTokenSource _identityRequest = new();
  private Guid _identity;
  private Guid? _selectedStop;
  private int _draftGeneration;
  private string _tab = "Overview";
  private string? _error;
  private string? _pendingNavigation;
  private bool _loading;
  private bool _saving;
  private bool _dirty;
  private bool _uncertain;
  private bool _conflict;
  private bool _disposed;
  private bool _confirmReload;
  private bool _verifying;
  private bool _activityDirty;
  private bool _documentDirty;
  private bool _transferDirty;
  private bool _brokerBusy;
  private bool _correctionDirty;
  private bool _operationDirty;

  private bool Completed =>
    _workspace is not null && DispatchBoardRow.IsCompleted(_workspace.Load);
  private Guid? CurrentTruckId
  {
    get
    {
      var saved = _baseline ?? _workspace;
      if (saved is null)
        return null;
      var nativeStops = saved
        .Stops.Where(stop => stop.ExecutionLegId.HasValue)
        .ToArray();
      if (nativeStops.Length == 0)
        return saved.Load.TruckId is { } truck && truck != Guid.Empty
          ? truck
          : null;
      // A confirmed release ends the leg even if earlier actuals are missing.
      var releasedLegs = nativeStops
        .Where(stop =>
          stop.Transfer is { Status: "confirmed", Action: "drop" or "release" }
        )
        .Select(stop => stop.ExecutionLegId!.Value)
        .ToHashSet();
      var nativeIds = nativeStops
        .Where(stop => !releasedLegs.Contains(stop.ExecutionLegId!.Value))
        .Select(stop => stop.Id)
        .ToHashSet();
      var stops = saved
        .Load.Stops.Where(stop =>
          nativeIds.Contains(stop.Id) && !stop.DriverOnly
        )
        .OrderBy(stop => stop.Sequence)
        .ToArray();
      var current =
        stops.FirstOrDefault(stop => !stop.IsCompleted)
        ?? stops.LastOrDefault();
      return current?.TruckId is { } currentTruck && currentTruck != Guid.Empty
        ? currentTruck
        : null;
    }
  }
  private bool HasUnsaved => _dirty || _uncertain;
  private bool HasToolbarDraft =>
    HasUnsaved || _operationDirty || _correctionDirty || _stopDrafts.HasChanges;
  private bool ToolbarSaving =>
    _saving
    || _operationDirty && _operationEditor?.IsSaving == true
    || _correctionDirty && _correctionEditor?.IsSaving == true;
  private bool ToolbarRetry =>
    _uncertain
    || _stopDrafts.PendingStopId.HasValue
    || _correctionDirty && _correctionEditor?.HasPendingSave == true;
  private bool AnyDraft =>
    HasUnsaved
    || _activityDirty
    || _documentDirty
    || _transferDirty
    || _brokerBusy
    || _correctionDirty
    || _stopDrafts.HasChanges
    || _operationDirty;
  private bool DraftLocked =>
    _saving
    || _uncertain
    || _verifying
    || _transferDirty
    || _brokerBusy
    || _correctionDirty
    || _stopDrafts.HasChanges
    || _operationDirty;
  private bool CanSave =>
    _workspace?.CanEdit == true
    && !_brokerBusy
    && !_transferDirty
    && HasToolbarDraft
    && !ToolbarSaving
    && !_loading
    && !_conflict
    && !_verifying
    && (
      _stopDrafts.HasChanges ? _stopDrafts.CanSubmit
      : _operationDirty ? _operationEditor?.CanSubmit == true
      : _correctionDirty ? _correctionEditor?.CanSubmit == true
      : true
    );
  private string SaveStatus =>
    ToolbarSaving ? "Saving…"
    : ToolbarRetry ? "Save not confirmed"
    : HasToolbarDraft ? "Unsaved changes"
    : "All changes saved";
  private string LoadLabel =>
    _workspace is null
      ? "Load"
      : LoadNumberDisplay.Format(
        _workspace.Load.LoadNumber,
        DisplaySettings?.LoadNumberPrefix
      );
  private string PageTitle => $"{LoadLabel} · PulsR";

  protected override async Task OnParametersSetAsync()
  {
    if (_identity == Id)
    {
      if (StopId.HasValue)
        _selectedStop = StopId;
      return;
    }
    _identityRequest.Cancel();
    _identityRequest.Dispose();
    _identityRequest = new();
    _identity = Id;
    _resourceOptions = new();
    _workspace = null;
    _baseline = null;
    _pendingSave = null;
    _selectedStop = StopId;
    _tab = "Overview";
    _dirty = _uncertain = _saving = _loading = _conflict = false;
    _verifying = false;
    _activityDirty = _documentDirty = _confirmReload = false;
    _transferDirty = false;
    _brokerBusy = false;
    _correctionDirty = false;
    _stopDrafts.Clear();
    _operationDirty = false;
    _error = null;
    _pendingNavigation = null;
    await LoadAsync();
  }

  private async Task LoadAsync()
  {
    if (_loading || _disposed)
      return;
    var owner = _identityRequest;
    var id = Id;
    _loading = true;
    _error = null;
    var result = await Api.GetAsync<DispatchWorkspaceResponse>(
      $"api/dispatch/{id}/workspace",
      owner.Token
    );
    if (!Owns(owner, id))
      return;
    _loading = false;
    if (!result.Success || result.Response is null)
      _error = result.ErrorMessage;
    else if (result.Response.Load.Id != id)
      _error = "The returned load does not match this page. Please retry.";
    else
      Accept(result.Response);
  }

  private void Accept(DispatchWorkspaceResponse workspace)
  {
    _baseline = Clone(workspace);
    _workspace = Clone(workspace);
    _dirty = _uncertain = _conflict = false;
    _pendingSave = null;
    _draftGeneration++;
    if (!_workspace.Stops.Any(x => x.Id == _selectedStop))
      _selectedStop =
        _workspace.Stops.FirstOrDefault(x => x.CanEdit)?.Id
        ?? _workspace.Stops.FirstOrDefault()?.Id;
  }

  private void MarkChanged()
  {
    if (DraftLocked || _workspace?.CanEdit != true)
      return;
    _dirty = true;
    _pendingSave = null;
  }

  private async Task SaveAsync()
  {
    if (!CanSave || _workspace is null)
      return;
    _error = null;
    if (_stopEditor is not null && !_stopEditor.Validate(out _error))
    {
      _tab = "Overview";
      return;
    }
    var owner = _identityRequest;
    var id = Id;
    _pendingSave ??= new()
    {
      ExpectedRevision = _workspace.Revision,
      SourceFingerprint = _workspace.SourceFingerprint,
      IdempotencyKey = Guid.NewGuid(),
      Metadata = Clone(_workspace.Metadata),
      Stops = Clone(_workspace.Stops),
    };
    _saving = true;
    var result = await Api.PutAsync<
      UpdateDispatchWorkspaceRequest,
      DispatchWorkspaceResponse
    >($"api/dispatch/{id}/workspace", _pendingSave, owner.Token);
    if (!Owns(owner, id))
      return;
    _saving = false;
    if (!result.Success || result.Response?.Load.Id != id)
    {
      _error = string.IsNullOrWhiteSpace(result.ErrorMessage)
        ? "Could not confirm this save."
        : result.ErrorMessage;
      _conflict = result.HttpStatusCode == HttpStatusCode.Conflict;
      _uncertain =
        result.HttpStatusCode is null or HttpStatusCode.RequestTimeout
        || (int?)result.HttpStatusCode >= 500
        || result.Success;
      if (!_uncertain)
        _pendingSave = null;
      return;
    }
    InvalidatePlanning(_baseline?.Load);
    Accept(result.Response);
    InvalidatePlanning(_workspace.Load);
    StateHasChanged();
    await RefreshSavedAsync(owner, id, _draftGeneration);
  }

  private async Task SaveToolbarAsync()
  {
    if (!CanSave)
      return;
    if (_stopDrafts.HasChanges)
      await SaveStopDraftsAsync();
    else if (_operationDirty && _operationEditor is not null)
      await _operationEditor.SubmitAsync();
    else if (_correctionDirty && _correctionEditor is not null)
      await _correctionEditor.SubmitAsync();
    else
      await SaveAsync();
  }

  private async Task SaveStopDraftsAsync()
  {
    if (_workspace is null)
      return;
    var owner = _identityRequest;
    var id = Id;
    _saving = true;
    _error = null;
    try
    {
      foreach (var (stopId, draft) in _stopDrafts.Ordered.ToArray())
      {
        var request = draft.Request;
        if (_stopDrafts.PendingStopId != stopId)
        {
          request.ExpectedRevision = _workspace.Revision;
          request.SourceFingerprint = _workspace.SourceFingerprint;
          _stopDrafts.PendingStopId = stopId;
        }
        var result = await Api.PutAsync<
          StopCorrectionRequest,
          DispatchWorkspaceResponse
        >($"api/dispatch/{id}/stops/{stopId}/correction", request, owner.Token);
        if (!Owns(owner, id))
          return;
        if (!result.Success || result.Response?.Load.Id != id)
        {
          var sequence = _workspace.Stops.Find(x => x.Id == stopId)?.Sequence;
          _error =
            $"Stop {sequence}: "
            + (result.ErrorMessage ?? "Save not confirmed. Retry saving.")
            + " Remaining stop changes are still in your draft.";
          if (result.HttpStatusCode == HttpStatusCode.BadRequest)
            _stopDrafts.PendingStopId = null;
          _selectedStop = stopId;
          return;
        }
        InvalidatePlanning(_workspace.Load);
        _stopDrafts.Entries.Remove(stopId);
        _stopDrafts.PendingStopId = null;
        Accept(result.Response);
        InvalidatePlanning(_workspace.Load);
      }
    }
    finally
    {
      if (Owns(owner, id))
        _saving = false;
    }
  }

  private async Task DiscardToolbarAsync()
  {
    if (ToolbarSaving)
      return;
    if (_stopDrafts.HasChanges)
    {
      var reload = _stopDrafts.PendingStopId.HasValue;
      _stopDrafts.Clear();
      if (reload)
        await LoadAsync();
      else if (_baseline is not null)
        Accept(_baseline);
    }
    else if (_operationDirty && _operationEditor is not null)
      await _operationEditor.DiscardAsync();
    else if (_correctionDirty && _correctionEditor is not null)
      await _correctionEditor.DiscardAsync();
    else
      Discard();
  }

  private async Task RefreshSavedAsync(
    CancellationTokenSource owner,
    Guid id,
    int generation
  )
  {
    var result = await Api.GetAsync<DispatchWorkspaceResponse>(
      $"api/dispatch/{id}/workspace",
      owner.Token
    );
    if (
      !Owns(owner, id)
      || generation != _draftGeneration
      || AnyDraft
      || _workspace is null
    )
      return;
    if (
      result.Success
      && result.Response is { } workspace
      && workspace.Load.Id == id
    )
    {
      if (
        workspace.Revision != _workspace.Revision
        || workspace.SourceFingerprint != _workspace.SourceFingerprint
      )
      {
        _conflict = true;
        _error = "The saved load changed again. Reload its latest version.";
        return;
      }
      _workspace.Load = Clone(workspace.Load);
      if (_baseline is not null)
        _baseline.Load = Clone(workspace.Load);
    }
  }

  private void Discard()
  {
    if (_saving || _baseline is null)
      return;
    if (_uncertain)
    {
      _confirmReload = true;
      return;
    }
    Accept(_baseline);
    _error = null;
  }

  private async Task ExecutionChangedAsync()
  {
    if (HasUnsaved || _stopDrafts.HasChanges || _disposed)
      return;
    InvalidatePlanning(_workspace?.Load);
    await LoadAsync();
    InvalidatePlanning(_workspace?.Load);
  }

  private void InvalidatePlanning(DispatchResponse? load)
  {
    if (load is null)
      return;
    foreach (
      var truck in load
        .Stops.Select(x => x.TruckId)
        .Append(load.TruckId)
        .Where(x => x.HasValue)
        .Select(x => x!.Value)
        .DefaultIfEmpty(Guid.Empty)
        .Distinct()
    )
      PlanningCache.Invalidate(truck, load.Id);
  }

  private DispatchStopMap? _stopMap;

  private void SelectStop(Guid? id) => _selectedStop = id;

  private Task ActivateStop(Guid id) =>
    _stopMap?.ActivateStopAsync(id) ?? Task.CompletedTask;

  private async Task OpenSwitch(Guid id)
  {
    if (HasUnsaved || DraftLocked || _switchEditor is null)
      return;
    _selectedStop = id;
    await _switchEditor.OpenAsync(id);
  }

  private async Task StartTransferAsync(DispatchTransferStart request)
  {
    if (HasUnsaved || DraftLocked || _switchEditor is null)
      return;
    _selectedStop = request.StopId;
    await _switchEditor.OpenAsync(request.StopId, request.Kind);
  }

  private async Task<
    RequestResponseDTO<VerifiedDispatchAddress>
  > LookupAddressAsync(string query, CancellationToken ct)
  {
    using var linked = CancellationTokenSource.CreateLinkedTokenSource(
      ct,
      _identityRequest.Token
    );
    return await Api.PostAsync<
      VerifyDispatchAddressRequest,
      VerifiedDispatchAddress
    >(
      $"api/dispatch/{Id}/workspace/verify-address",
      new() { Address = query },
      linked.Token
    );
  }

  private async Task VerifyAddressAsync(Guid stopId)
  {
    if (DraftLocked || _workspace?.CanEdit != true)
      return;
    var stop = _workspace.Stops.FirstOrDefault(x => x.Id == stopId);
    if (stop?.CanEdit != true)
      return;
    var owner = _identityRequest;
    var id = Id;
    var draft = _workspace;
    _verifying = true;
    _error = null;
    var result = await Api.PostAsync<
      VerifyDispatchAddressRequest,
      VerifiedDispatchAddress
    >(
      $"api/dispatch/{id}/workspace/verify-address",
      new()
      {
        Address = stop.Address,
        City = stop.City,
        Province = stop.Province,
        Country = stop.Country,
        ZipCode = stop.ZipCode,
      },
      owner.Token
    );
    if (!Owns(owner, id))
      return;
    _verifying = false;
    if (!ReferenceEquals(_workspace, draft))
      return;
    if (!result.Success || result.Response is null)
    {
      _error = result.ErrorMessage;
      return;
    }
    var address = result.Response;
    stop.Address = address.Address;
    stop.City = address.City;
    stop.Province = address.Province;
    stop.Country = address.Country;
    stop.ZipCode = address.ZipCode;
    stop.Latitude = address.Latitude;
    stop.Longitude = address.Longitude;
    MarkChanged();
  }

  private void SelectTab(string tab) => _tab = tab;

  private void RequestReload() => _confirmReload = true;

  private void CancelReload() => _confirmReload = false;

  private async Task ReloadAsync()
  {
    _confirmReload = false;
    await LoadAsync();
  }

  private void BeforeNavigation(LocationChangingContext context)
  {
    if (!AnyDraft)
      return;
    context.PreventNavigation();
    _pendingNavigation = context.TargetLocation;
  }

  private void Stay() => _pendingNavigation = null;

  private void Leave()
  {
    var target = _pendingNavigation;
    _pendingNavigation = null;
    _dirty = _uncertain = false;
    _activityDirty = _documentDirty = false;
    if (target is not null)
      Navigation.NavigateTo(target);
  }

  private void ActivityDraftChanged(bool value) => _activityDirty = value;

  private void DocumentDraftChanged(bool value) => _documentDirty = value;

  private void TransferDraftChanged(bool value) => _transferDirty = value;

  private void CorrectionDraftChanged(bool value) => _correctionDirty = value;

  private void OperationDraftChanged(bool value) => _operationDirty = value;

  private void BrokerBusyChanged(bool value) => _brokerBusy = value;

  private bool Owns(CancellationTokenSource owner, Guid id) =>
    !_disposed
    && _identityRequest == owner
    && !owner.IsCancellationRequested
    && Id == id;

  private static T Clone<T>(T value) =>
    JsonSerializer.Deserialize<T>(JsonSerializer.Serialize(value))!;

  private static string Value(string? value) =>
    string.IsNullOrWhiteSpace(value) ? "—" : value;

  private static string Timestamp(DateTime value) =>
    DispatchWorkspaceStopDisplay.Timestamp(value);

  public void Dispose()
  {
    _disposed = true;
    _identityRequest.Cancel();
    _identityRequest.Dispose();
  }
}
