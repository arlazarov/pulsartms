using System.Net;
using Client.Models.DTO.Dispatch.Workspace;
using Client.Models.DTO.Mileage;
using Client.Services;
using Microsoft.AspNetCore.Components;

namespace Client.Pages.Dispatch;

public partial class DispatchStopCorrection
{
  [Inject]
  private ApiService Api { get; set; } = default!;

  [Parameter, EditorRequired]
  public DispatchWorkspaceResponse Workspace { get; set; } = new();

  [Parameter, EditorRequired]
  public DispatchWorkspaceStop Stop { get; set; } = new();

  [Parameter]
  public bool Disabled { get; set; }

  [Parameter]
  public bool PageActions { get; set; }
  public bool IsSaving => _saving;
  public bool HasPendingSave => _pending is not null;
  public bool CanSubmit =>
    !Disabled
    && !_saving
    && !_disposed
    && Stop.CanCorrect
    && (Dirty || _pending is not null)
    && (
      _pending is not null
      || !AssignmentChanged
      || !_loading && _resourceError is null && ScopeError is null
    );

  public async Task SubmitAsync()
  {
    await SaveAsync();
    if (!_disposed)
      StateHasChanged();
  }

  public async Task DiscardAsync()
  {
    await CancelAsync();
    if (!_disposed)
      StateHasChanged();
  }

  [Parameter]
  public EventCallback<bool> DraftChanged { get; set; }

  [Parameter]
  public EventCallback Changed { get; set; }

  [Parameter]
  public DispatchStopDrafts? StopDrafts { get; set; }

  [Parameter]
  public DispatchResourceOptions? ResourceOptions { get; set; }

  [Parameter]
  public CancellationToken ResourceCancellation { get; set; }

  private readonly DispatchResourceOptions _localOptions = new();

  private IReadOnlyList<KeyValuePair<Guid, string>> TruckOptions =>
    _trucks.Select(x => KeyValuePair.Create(x.Id, x.UnitNumber)).ToList();

  private IReadOnlyList<KeyValuePair<Guid, string>> TrailerOptions =>
    _trailers.Select(x => KeyValuePair.Create(x.Id, x.UnitNumber)).ToList();

  private IReadOnlyList<KeyValuePair<Guid, string>> DriverOptions =>
    _drivers.Select(x => KeyValuePair.Create(x.Id, x.Name)).ToList();

  private bool EditingStops => StopDrafts?.HasChanges == true;

  private readonly CancellationTokenSource _lifetime = new();
  private DispatchWorkspaceResponse? _opened;
  private bool _saving,
    _loading,
    _disposed;
  private string _status = "pending",
    _initialStatus = "pending",
    _scope = "current";
  private Guid? _truck,
    _trailer,
    _driver,
    _coDriver;
  private Guid? _initialTruck,
    _initialTrailer,
    _initialDriver,
    _initialCoDriver;
  private Guid _dispatchId,
    _stopId,
    _from,
    _to;
  private long _revision;
  private string _fingerprint = "";
  private string? _error,
    _resourceError;
  private StopCorrectionRequest? _pending;
  private List<MileageUnitOption> _trucks = [],
    _trailers = [];
  private List<MileageDriverOption> _drivers = [];
  private bool AssignmentChanged =>
    _truck != _initialTruck
    || _trailer != _initialTrailer
    || _driver != _initialDriver
    || _coDriver != _initialCoDriver;
  private bool Dirty => AssignmentChanged || _status != _initialStatus;

  protected override async Task OnParametersSetAsync()
  {
    if (
      _opened is null
      || _dispatchId != Workspace.Load.Id
      || _stopId != Stop.Id
      || !ReferenceEquals(_opened, Workspace) && !Dirty && _pending is null
    )
    {
      Reset();
      if (StopDrafts?.Entries.TryGetValue(Stop.Id, out var draft) == true)
      {
        _initialStatus = draft.InitialStatus;
        _status =
          draft.Request.Completion == "keep"
            ? _initialStatus
            : draft.Request.Completion;
        _initialDriver = draft.InitialDriverId;
        _initialCoDriver = draft.InitialCoDriverId;
        _driver = draft.Request.DriverId;
        _coDriver = draft.Request.CoDriverId;
        _scope = draft.Request.AssignmentScope;
        _from = draft.Request.FromStopId ?? Stop.Id;
        _to = draft.Request.ToStopId ?? Stop.Id;
      }
      else if (StopDrafts is not null)
      {
        _status = _initialStatus = StopDrafts.IsCompleted(
          Stop,
          Workspace.Stops,
          _status == "completed"
        )
          ? "completed"
          : "pending";
        var projected = StopDrafts.Names(Stop, Workspace.Stops);
        _driver = _initialDriver = projected.DriverId;
        _coDriver = _initialCoDriver = projected.CoDriverId;
      }
      await LoadResourcesAsync();
    }
  }

  private void Reset()
  {
    _opened = Workspace;
    _dispatchId = Workspace.Load.Id;
    _stopId = Stop.Id;
    _revision = Workspace.Revision;
    _fingerprint = Workspace.SourceFingerprint;
    _status = _initialStatus =
      Workspace.Load.Stops.FirstOrDefault(x => x.Id == Stop.Id)?.IsCompleted
      == true
        ? "completed"
        : "pending";
    _truck = _initialTruck = Stop.TruckId;
    _trailer = _initialTrailer = Stop.TrailerId;
    _driver = _initialDriver = Stop.DriverId;
    _coDriver = _initialCoDriver = Stop.CoDriverId;
    _scope = Stop.Transfer is null ? "stop" : "current";
    _from = Stop.Id;
    _to = Workspace.Stops.LastOrDefault()?.Id ?? Stop.Id;
    _pending = null;
    _error = null;
    EnsureCurrentOptions();
  }

  private void EnsureCurrentOptions()
  {
    if (Stop.TruckId is { } truck && _trucks.All(x => x.Id != truck))
      _trucks.Add(new(truck, Stop.TruckNumber ?? "", false));
    if (Stop.TrailerId is { } trailer && _trailers.All(x => x.Id != trailer))
      _trailers.Add(new(trailer, Stop.TrailerNumber ?? "", false));
    if (Stop.DriverId is { } driver && _drivers.All(x => x.Id != driver))
      _drivers.Add(new(driver, Stop.DriverName ?? "", false));
    if (Stop.CoDriverId is { } coDriver && _drivers.All(x => x.Id != coDriver))
      _drivers.Add(new(coDriver, Stop.CoDriverName ?? "", false));
  }

  private async Task LoadResourcesAsync()
  {
    if (_loading || _disposed)
      return;
    _loading = true;
    var owner = _stopId;
    try
    {
      var result = await (ResourceOptions ?? _localOptions).ReadAsync(
        Api,
        ResourceOptions is null ? _lifetime.Token : ResourceCancellation
      );
      if (_disposed || owner != _stopId)
        return;
      if (!result.Success)
      {
        _resourceError =
          "Resources could not be loaded. Status can still be changed.";
        return;
      }
      _trucks = result.Trucks.Response!.Items.ToList();
      _trailers = result.Trailers.Response!.Items.ToList();
      _drivers = result.Drivers.Response!.Items.ToList();
      EnsureCurrentOptions();
      _resourceError = null;
    }
    catch (OperationCanceledException) when (_lifetime.IsCancellationRequested)
    { }
    finally
    {
      _loading = false;
    }
  }

  private List<DispatchWorkspaceStop> AffectedStops()
  {
    var rows = Workspace.Stops;
    if (_scope == "stop")
      return rows.Where(x => x.Id == Stop.Id).ToList();
    if (_scope == "current")
      return rows.Where(x => x.ExecutionLegId == Stop.ExecutionLegId).ToList();
    if (_scope == "all")
      return rows.ToList();
    var from = rows.FindIndex(x =>
      x.Id == (_scope == "onward" ? Stop.Id : _from)
    );
    var to =
      _scope == "onward" ? rows.Count - 1 : rows.FindIndex(x => x.Id == _to);
    return from < 0 || to < from
      ? []
      : rows.Skip(from).Take(to - from + 1).ToList();
  }

  private string? ScopeError
  {
    get
    {
      if (!AssignmentChanged)
        return null;
      var affected = AffectedStops();
      if (affected.Count == 0)
        return "Select a valid stop range.";
      if (_truck == _initialTruck && _trailer == _initialTrailer)
        return null;
      var legs = affected.Select(x => x.ExecutionLegId).ToHashSet();
      return Workspace.Stops.Any(x =>
        legs.Contains(x.ExecutionLegId) && !affected.Contains(x)
      )
        ? "Choose the full assignment section. Splitting an existing section is not supported yet."
        : null;
    }
  }

  private string AssignmentPreview
  {
    get
    {
      if (Stop.Transfer is not null && _trailer != _initialTrailer)
        return _trailer != _initialTrailer
          ? "Trailer changes apply to both linked Drop and Hook assignments. Other changes apply to this side. Confirmed events stay unchanged."
          : "Changes apply to this side of the transfer. The other side and confirmed events stay unchanged.";
      var names = new List<string>();
      if (_truck != _initialTruck)
        names.Add("Truck");
      if (_trailer != _initialTrailer)
        names.Add("Trailer");
      if (_driver != _initialDriver)
        names.Add("Driver");
      if (_coDriver != _initialCoDriver)
        names.Add("Co-driver");
      return string.Join(", ", names)
        + " → stops "
        + string.Join(", ", AffectedStops().Select(x => x.Sequence))
        + ". Other resources stay unchanged.";
    }
  }

  private Task DriverChangedAsync()
  {
    if (
      StopDrafts is not null
      && !StopDrafts.Entries.ContainsKey(Stop.Id)
      && _truck == _initialTruck
      && _trailer == _initialTrailer
    )
      _scope = "stop";
    return DraftChangedAsync();
  }

  private async Task DraftChangedAsync()
  {
    _error = null;
    if (
      StopDrafts is not null
      && _truck == _initialTruck
      && _trailer == _initialTrailer
    )
    {
      StopDrafts.Set(
        Stop.Id,
        Dirty
          ? new(
            CreateRequest(),
            _drivers.FirstOrDefault(x => x.Id == _driver)?.Name ?? "",
            _drivers.FirstOrDefault(x => x.Id == _coDriver)?.Name ?? "",
            !AssignmentChanged
              || !_loading && _resourceError is null && ScopeError is null,
            _initialDriver,
            _initialCoDriver,
            _initialStatus
          )
          : null
      );
      await DraftChanged.InvokeAsync(false);
      return;
    }
    await DraftChanged.InvokeAsync(Dirty);
  }

  private async Task ToggleStatusAsync()
  {
    if (Disabled || _saving || _pending is not null || !Stop.CanCorrect)
      return;
    _status = _status == "completed" ? "pending" : "completed";
    await DraftChangedAsync();
  }

  private async Task CancelAsync()
  {
    if (_saving)
      return;
    var reload = _pending is not null;
    Reset();
    await DraftChanged.InvokeAsync(false);
    if (reload)
      await Changed.InvokeAsync();
  }

  private async Task SaveAsync()
  {
    if (
      Disabled
      || !Stop.CanCorrect
      || _saving
      || _disposed
      || !Dirty && _pending is null
    )
      return;
    if (_pending is null)
    {
      if (
        AssignmentChanged
        && (_loading || _resourceError is not null || ScopeError is not null)
      )
        return;
      if (AssignmentChanged && !_truck.HasValue)
      {
        _error = "Select a truck.";
        return;
      }
      _pending = CreateRequest();
    }
    var request = _pending;
    _saving = true;
    try
    {
      await DraftChanged.InvokeAsync(Dirty || _pending is not null);
      var result = await Api.PutAsync<
        StopCorrectionRequest,
        DispatchWorkspaceResponse
      >(
        $"api/dispatch/{_dispatchId}/stops/{_stopId}/correction",
        request,
        _lifetime.Token
      );
      if (_disposed || Workspace.Load.Id != _dispatchId || Stop.Id != _stopId)
        return;
      if (!result.Success)
      {
        _error =
          result.ErrorMessage
          ?? "Save not confirmed. Retry with the same request.";
        if (result.HttpStatusCode == HttpStatusCode.BadRequest)
          _pending = null;
        return;
      }
      Reset();
      await DraftChanged.InvokeAsync(false);
      await Changed.InvokeAsync();
    }
    catch (OperationCanceledException) when (_lifetime.IsCancellationRequested)
    { }
    finally
    {
      _saving = false;
      if (!_disposed)
        await DraftChanged.InvokeAsync(Dirty || _pending is not null);
    }
  }

  private StopCorrectionRequest CreateRequest() =>
    new()
    {
      ExpectedRevision = _revision,
      SourceFingerprint = _fingerprint,
      IdempotencyKey = Guid.NewGuid(),
      Completion =
        Stop.Transfer is not null || _status == _initialStatus
          ? "keep"
          : _status,
      ChangeAssignment = AssignmentChanged,
      AssignmentScope = _scope,
      FromStopId = _from,
      ToStopId = _to,
      ChangeTruck = _truck != _initialTruck,
      ChangeTrailer = _trailer != _initialTrailer,
      ChangeDriver = _driver != _initialDriver,
      ChangeCoDriver = _coDriver != _initialCoDriver,
      TruckId = _truck,
      TrailerId = _trailer,
      DriverId = _driver,
      CoDriverId = _coDriver,
    };

  public void Dispose()
  {
    _disposed = true;
    _lifetime.Cancel();
    _lifetime.Dispose();
    GC.SuppressFinalize(this);
  }
}
