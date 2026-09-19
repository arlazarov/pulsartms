using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using Client.Models;
using Client.Models.DTO;
using Client.Models.DTO.Fleet;
using Client.Models.DTO.Planning;
using Client.Services;
using Client.Shared;
using Client.Shared.Search;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.JSInterop;

namespace Client.Pages.FleetMap;

public partial class FleetMap : IAsyncDisposable
{
  [CascadingParameter]
  public DispatchSettingsState? DisplaySettings { get; set; }

  [CascadingParameter]
  public DisplayUnits Units { get; set; } = DisplayUnits.Default;
  private string? _displayedDistanceUnit;
  private string? _displayedLoadNumberPrefix;
  private (Guid? Truck, Guid? Dispatch)? _selectionParameters;
  private string? _addressCopyMessage;
  private DriverHosClocks? _hos;

  private async Task CopyTextAsync(string value)
  {
    try
    {
      await JS.InvokeVoidAsync("navigator.clipboard.writeText", value);
      _addressCopyMessage = null;
    }
    catch (JSException)
    {
      _addressCopyMessage = "Could not copy. Please try again.";
    }
  }

  [Inject]
  private PlanningDisplayCache PlanningCache { get; set; } = default!;

  [Inject]
  private HttpClient Http { get; set; } = default!;

  [Inject]
  private IJSRuntime JS { get; set; } = default!;

  [Inject]
  private IConfiguration Configuration { get; set; } = default!;

  [SupplyParameterFromQuery]
  public Guid? TruckId { get; set; }

  [SupplyParameterFromQuery]
  public Guid? DispatchId { get; set; }
  private double? _displayRemainingMiles;
  private double? _displayProgressMiles;
  private DateTime? _retainedProgressUntil;
  private readonly FleetRouteDisplayMemory _routeDisplay = new();
  private bool RouteInputsChanged => _routeState?.Plan?.InputsChanged == true;
  private RoutePlanningState? DisplayRouteState =>
    _routeDisplay.Display(
      _routeState,
      Clock.GetUtcNow().UtcDateTime,
      _etaRefreshPending
    );
  private bool RetainingRouteDisplay =>
    _routeDisplay.IsRetaining(
      _routeState,
      Clock.GetUtcNow().UtcDateTime,
      _etaRefreshPending
    );
  private bool DisplayedProgressIsCurrent =>
    _retainedProgressUntil is null
    || Clock.GetUtcNow().UtcDateTime < _retainedProgressUntil;
  private double? RemainingMiles =>
    RouteInputsChanged ? null
    : RetainingRouteDisplay ? _routeDisplay.RetainedRemainingMiles
    : (DisplayedProgressIsCurrent ? _displayRemainingMiles : null)
      ?? _routeState?.Progress?.RemainingMiles;
  private double? NextStopMiles =>
    RouteInputsChanged ? null
    : RetainingRouteDisplay ? _routeDisplay.RetainedNextStopMiles
    : _routeState?.Plan?.RemainingToNextStop(
      (DisplayedProgressIsCurrent ? _displayProgressMiles : null)
        ?? _routeState?.Progress?.ProgressMiles
    );
  private bool _showTruckInfo;
  private bool _followingTruck;
  private bool _mobileFiltersOpen;
  private bool _selectionDismissed;
  private List<TruckLocationMapDto> _trucks = [];
  private List<TruckLocationMapDto> _truckPoints = [];
  private string TruckSearch { get; set; } = "";
  private Guid? _searchFocused;
  private List<SearchSuggestion> SearchOptions =>
    MatchingTrucks
      .Select(x => new SearchSuggestion(
        x.UnitNumber,
        $"{x.DriverName} · {x.TrailerNumber}"
      ))
      .ToList();
  private List<TruckLocationMapDto> MatchingTrucks =>
    TruckMapSearch.Filter(_trucks, TruckSearch);
  private TruckLocationMapDto? SelectedTruck =>
    _trucks.FirstOrDefault(x => x.TruckId == _activeTruckId);
  private Guid? SelectedDispatchId =>
    (_planningDispatchId ?? _routeState?.Plan?.DispatchId ?? _activeDispatchId)
      is { } id
    && id != Guid.Empty
      ? id
      : null;
  private RoutePlanningState? _routeState;
  private Guid? _activeTruckId;
  private Guid? _activeDispatchId;
  private Guid? _planningDispatchId;
  private bool _routeLoading;
  private bool _etaRefreshPending;
  private bool _recalculatingFuel;
  private CancellationTokenSource? _routeRequest;

  [Inject]
  private ApiService Api { get; set; } = default!;

  [Inject]
  private TimeProvider Clock { get; set; } = default!;
  private string? RouteError;
  private string? _routeValidationUrl;
  private DateTimeOffset _routeValidationRetryAt;
  private string? VisibleRouteError =>
    RouteMessageDisplay.For(
      RouteError,
      DisplayRouteState?.Plan
        is { InputsChanged: false, Tracking.AllStopsPassed: false } plan
        && plan.DispatchId == SelectedDispatchId
        && (_activeTruckId is null || plan.TruckId == _activeTruckId)
    );

  private Guid? _focusedTruckId;
  private string? FocusError { get; set; }
  private ElementReference _mapElement;
  private FleetMapSession<FleetMap>? _session;
  private IJSObjectReference? _map => _session?.Map;
  private FleetStationLayer? _stations;
  private readonly CancellationTokenSource _lifetime = new();
  private readonly PageVisibility _visibility = new();
  private Task? _initializationTask;
  private Task? _truckPollingTask;
  private bool _disposed;
  private bool _initializing;
  private string? MapError { get; set; }
  private string? TruckError { get; set; }
  private string? StationError => _stations?.Error ?? _stations?.PriceError;
  private DateOnly SelectedDate { get; set; } =
    DateOnly.FromDateTime(DateTime.Today);
  private bool UseIfta { get; set; }
  private bool ShowFuelStations { get; set; }
  private bool ShowTraffic { get; set; } = true;

  protected override async Task OnParametersSetAsync()
  {
    if (_map is not null && _displayedDistanceUnit != Units.Distance)
    {
      _displayedDistanceUnit = Units.Distance;
      await _map.InvokeVoidAsync("setDistanceUnit", Units.Distance);
    }
    var prefix = DisplaySettings?.LoadNumberPrefix;
    var prefixChanged = !string.Equals(
      _displayedLoadNumberPrefix,
      prefix,
      StringComparison.Ordinal
    );
    _displayedLoadNumberPrefix = prefix;
    if (_selectionParameters != (TruckId, DispatchId))
    {
      _selectionParameters = (TruckId, DispatchId);
      _selectionDismissed = false;
      if (
        _map is not null
        && DispatchId.HasValue
        && DispatchId != _activeDispatchId
      )
        await SelectRouteAsync(TruckId, DispatchId);
      if (!TruckId.HasValue)
      {
        _focusedTruckId = null;
        FocusError = null;
      }
      else if (TruckId != _focusedTruckId)
        await FocusTruckAsync();
    }
    if (prefixChanged && _map is not null)
      await SendMapLoadReferenceAsync();
  }

  private async Task FocusTruckAsync()
  {
    if (
      _selectionDismissed
      || _map is null
      || _disposed
      || !TruckId.HasValue
      || TruckId == _focusedTruckId
    )
      return;
    if (
      await _map.InvokeAsync<bool>(
        "focusTruck",
        TruckId.Value.ToString(),
        (int?)null,
        true
      )
    )
    {
      _focusedTruckId = TruckId;
      FocusError = null;
      await SelectRouteAsync(TruckId, DispatchId);
    }
    else
      FocusError = "This truck has no location available on the map.";
  }

  protected override async Task OnAfterRenderAsync(bool firstRender)
  {
    await PublishInspectorSuspensionAsync();
    if (!firstRender)
      return;
    await _visibility.StartAsync(JS);
    _hosPollingTask = HosRefreshLoop.RunAsync(
      RefreshHosAsync,
      _lifetime.Token,
      Clock,
      _visibility
    );
    await StartMapAsync();
  }

  private Task StartMapAsync()
  {
    if (_disposed || _initializing)
      return Task.CompletedTask;
    _initializationTask = InitializeMapAsync();
    return _initializationTask;
  }

  private async Task InitializeMapAsync()
  {
    _initializing = true;
    MapError = null;
    try
    {
      await RestoreMapPreferencesAsync();
      if (_disposed)
        return;
      await InvokeAsync(StateHasChanged);
      _session ??= new(JS, this);
      _inspectorVersion = 0;
      await _session.StartAsync(
        _mapElement,
        Configuration["GoogleMaps:ApiKey"],
        new
        {
          stationsVisible = ShowFuelStations,
          trafficVisible = ShowTraffic,
          useIfta = UseIfta,
          distanceUnit = Units.Distance,
          initialTruckId = TruckId,
        }
      );
      if (_disposed || _map is null)
        return;
      _displayedDistanceUnit = Units.Distance;
      await _map.InvokeVoidAsync("setDistanceUnit", Units.Distance);
      await _map.InvokeVoidAsync("setNextLoadsVisible", ShowNextLoads);
      if (_disposed)
        return;
      _stations = new(Http, _map);
      _initializing = false;
      await InvokeAsync(StateHasChanged);
      _truckPollingTask = PollTrucksAsync(_lifetime.Token);
      _ = OnDateChanged();
    }
    catch (Exception ex) when (IsLoadError(ex))
    {
      if (!_disposed)
      {
        var reason =
          ex is JSException ? ex.Message.Split('\n')[0] : ex.GetType().Name;
        reason = Regex.Replace(
          reason,
          @"https?://\S+",
          match =>
            Uri.TryCreate(match.Value, UriKind.Absolute, out var uri)
              ? uri.AbsolutePath
              : "[resource]"
        );
        MapError = $"The map could not be loaded. Please retry. ({reason})";
      }
    }
    finally
    {
      _initializing = false;
      if (!_disposed)
        await InvokeAsync(StateHasChanged);
    }
  }

  private Task PollTrucksAsync(CancellationToken cancellationToken) =>
    RefreshLoop.RunAsync(
      async token =>
      {
        var result = await Http.GetFromJsonAsync<
          ApiResponse<FleetLocationsMapDto>
        >("api/fleet/locations", token);
        if (_disposed)
          return;
        if (result?.Success != true || result.Response is null)
          throw new HttpRequestException("Fleet locations are unavailable.");
        _trucks = result.Response.Trucks;
        _truckPoints = result.Response.Points;
        await UpdateTruckSearchAsync();
        var nextLoadsTask = RefreshNextLoadsAsync(polled: true);
        await Task.WhenAll(nextLoadsTask, LoadRouteAsync(false));
        await FocusTruckAsync();
        if (
          !_selectionDismissed
          && DispatchId.HasValue
          && !_activeDispatchId.HasValue
          && !_activeTruckId.HasValue
        )
          await SelectRouteAsync(TruckId, DispatchId);
        TruckError = null;
        await InvokeAsync(StateHasChanged);
      },
      async _ =>
      {
        if (_disposed)
          return;
        if (_map is not null)
          await _map.InvokeVoidAsync("finishInitialView");
        TruckError =
          "Truck locations could not be updated. Showing the last available data; retrying automatically.";
        await InvokeAsync(StateHasChanged);
      },
      TimeSpan.FromSeconds(10),
      cancellationToken,
      Clock,
      _visibility
    );

  [JSInvokable]
  public Task OnTruckSelected(string id)
  {
    if (!Guid.TryParse(id, out var truckId))
      return Task.CompletedTask;
    _focusedTruckId = TruckId;
    FocusError = null;
    return SelectRouteAsync(truckId, null);
  }

  [JSInvokable]
  public Task OnFollowChanged(bool following)
  {
    if (_disposed)
      return Task.CompletedTask;
    _followingTruck = following;
    return InvokeAsync(StateHasChanged);
  }

  private async Task ToggleFollowAsync()
  {
    if (_map is null || _disposed || !_activeTruckId.HasValue)
      return;
    await _map.InvokeVoidAsync("setFollow", _activeTruckId.Value);
  }

  private bool CanShowRoute =>
    !MapOverlayOpen
    && DisplayRouteState?.Plan is { InputsChanged: false } plan
    && !plan.Tracking.AllStopsPassed
    && (
      plan.Route.Legs.Any(leg => leg.HasGeometry)
      || _routePublisher.HasGeometry(_map, plan)
    );

  private async Task ShowRouteAsync()
  {
    if (_disposed || _map is null || !CanShowRoute)
      return;
    await _map.InvokeVoidAsync("showRoute", InspectorTruckId?.ToString());
  }

  [JSInvokable]
  public Task OnRouteProgress(
    string truckId,
    double remainingMiles,
    double progressMiles
  )
  {
    if (
      _disposed
      || !Guid.TryParse(truckId, out var id)
      || id != _activeTruckId
    )
      return Task.CompletedTask;
    if (RouteInputsChanged || RetainingRouteDisplay)
      return Task.CompletedTask;
    if (!double.IsFinite(remainingMiles) || !double.IsFinite(progressMiles))
      return Task.CompletedTask;
    static bool SameDisplay(double? a, double? b) =>
      a.HasValue && b.HasValue
        ? Math.Round(a.Value) == Math.Round(b.Value)
          && Math.Round(a.Value * 1.609344) == Math.Round(b.Value * 1.609344)
        : a == b;
    var unchanged =
      SameDisplay(RemainingMiles, remainingMiles)
      && SameDisplay(
        NextStopMiles,
        _routeState?.Plan?.RemainingToNextStop(progressMiles)
      );
    _displayRemainingMiles = remainingMiles;
    _displayProgressMiles = progressMiles;
    _retainedProgressUntil = null;
    _routeDisplay.RecordProgress(remainingMiles, progressMiles);
    if (unchanged)
      return Task.CompletedTask;
    return InvokeAsync(StateHasChanged);
  }

  private async Task DeselectTruckAsync()
  {
    TruckSearch = "";
    _searchFocused = null;
    await ClearSelectionAsync();
  }

  private async Task ClearSelectionAsync()
  {
    _cameraOpen = false;
    if (_routeEditorDispatch.HasValue)
      await CloseRouteEditorAsync();
    ++_selectionVersion;
    _previewRequest?.Cancel();
    _previewRequest = null;
    _selectionDismissed = true;
    _showTruckInfo = false;
    _mobileTruckDetailsOpen = false;
    _inspectorMode = MapInspectorMode.Closed;
    _routeRequest?.Cancel();
    _routeRequest = null;
    _routeLoading = false;
    _etaRefreshPending = false;
    _recalculatingFuel = false;
    _fuelRevalidationPending = false;
    _activeTruckId = null;
    _arrivalMemory.Update(null, null, null);
    _loadDetailsVersion++;
    _loadDetails = null;
    ResetNextLoads();
    _followingTruck = false;
    _activeDispatchId = null;
    _planningDispatchId = null;
    _planningExecutionLegId = null;
    _planningAssignmentRevision = 0;
    SetRouteState(null);
    _hos = null;
    _displayRemainingMiles = null;
    _displayProgressMiles = null;
    RouteError = null;
    FocusError = null;
    if (_map is not null && !_disposed)
      await _map.InvokeVoidAsync("clearSelection");
  }

  private int _selectionVersion;

  private async Task SelectRouteAsync(
    Guid? truckId,
    Guid? dispatchId,
    bool fit = false
  )
  {
    if (_disposed || _map is null)
      return;
    if (_routeEditorDispatch.HasValue && truckId != _activeTruckId)
      await CloseRouteEditorAsync();
    ResetInspectedLoad();
    _selectionDismissed = false;
    _showTruckInfo = true;
    _mobileTruckDetailsOpen = false;
    _inspectorMode = MapInspectorMode.Truck;
    _addressCopyMessage = null;
    if (truckId == _activeTruckId && dispatchId == _activeDispatchId)
    {
      await _map.InvokeVoidAsync(
        "setInspectorMode",
        "truck",
        truckId?.ToString()
      );
      await _map.InvokeVoidAsync("clearNextLoadSelection");
      if (fit)
        await SendMapRouteAsync(true);
      await InvokeAsync(StateHasChanged);
      return;
    }
    var selectionVersion = ++_selectionVersion;
    _cameraOpen = false;
    _previewRequest?.Cancel();
    _previewRequest = null;
    _routeRequest?.Cancel();
    _routeRequest = null;
    _routeLoading = false;
    _etaRefreshPending = false;
    _displayRemainingMiles = null;
    _displayProgressMiles = null;
    _recalculatingFuel = false;
    _fuelRevalidationPending = false;
    _arrivalMemory.Update(null, null, null);
    _activeTruckId = truckId;
    _activeDispatchId = dispatchId;
    _planningDispatchId = null;
    _planningExecutionLegId = null;
    _planningAssignmentRevision = 0;
    SetRouteState(null);
    _loadDetailsVersion++;
    _loadDetails = null;
    _hos = SelectedHos(null);
    RouteError = null;
    ResetNextLoads();
    await _map.InvokeVoidAsync(
      "setInspectorMode",
      "truck",
      truckId?.ToString()
    );
    if (_disposed || selectionVersion != _selectionVersion)
      return;
    await _map.InvokeVoidAsync("clearNextLoads");
    if (_disposed || selectionVersion != _selectionVersion)
      return;
    if (_followingTruck)
    {
      _followingTruck = false;
      await _map.InvokeVoidAsync("setFollow", truckId, false);
      if (_disposed || selectionVersion != _selectionVersion)
        return;
    }
    var url = dispatchId.HasValue
      ? $"api/dispatch/{dispatchId}/planning/automatic"
      : $"api/fleet/trucks/{truckId}/planning";
    var cached = PlanningCache.Get(url);
    if (cached is null && dispatchId is null && truckId is { } previewTruck)
    {
      _routeLoading = true;
      var previewTask = LoadSavedPreviewAsync(previewTruck);
      await Task.WhenAll(
        SendMapRouteAsync(false),
        InvokeAsync(StateHasChanged)
      );
      cached = await previewTask;
      if (_disposed || selectionVersion != _selectionVersion)
        return;
    }
    _planningDispatchId = cached?.DispatchId;
    _planningExecutionLegId =
      cached?.ExecutionLegId ?? cached?.State?.Plan?.ExecutionLegId;
    _planningAssignmentRevision =
      cached?.State?.Plan?.AssignmentRevision
      ?? cached?.AssignmentRevision
      ?? 0;
    SetRouteState(cached?.State);
    var detailsTask = cached is { DispatchId: null }
      ? Task.CompletedTask
      : LoadDispatchDetailsAsync();
    _hos = SelectedHos(cached?.Hos);
    RouteError = null;
    _routeLoading = false;
    await InvokeAsync(StateHasChanged);
    await SendMapRouteAsync(fit);
    if (_disposed || selectionVersion != _selectionVersion)
    {
      await detailsTask;
      return;
    }
    var nextLoadsTask = RefreshNextLoadsAsync();
    await Task.WhenAll(
      detailsTask,
      nextLoadsTask,
      !_disposed && selectionVersion == _selectionVersion
        ? LoadRouteAsync(fit && _routeState?.Plan is null)
        : Task.CompletedTask
    );
  }

  private async Task OnTruckSearchChanged(string value)
  {
    TruckSearch = value;
    _searchFocused = null;
    await ClearSelectionAsync();
    await UpdateTruckSearchAsync();
  }

  private async Task SearchEnterAsync()
  {
    _searchFocused = null;
    await UpdateTruckSearchAsync();
  }

  private async Task ChooseSearchTruckAsync(string value)
  {
    TruckSearch = value;
    _searchFocused = null;
    await UpdateTruckSearchAsync();
  }

  private async Task UpdateTruckSearchAsync()
  {
    if (_map is null || _disposed)
      return;
    var matches = MatchingTrucks;
    await _map.InvokeVoidAsync("setTrucks", _trucks, _truckPoints);
    if (
      !string.IsNullOrWhiteSpace(TruckSearch)
      && matches.Count == 1
      && _searchFocused != matches[0].TruckId
    )
    {
      if (
        await _map.InvokeAsync<bool>(
          "focusTruck",
          matches[0].TruckId.ToString(),
          (int?)null
        )
      )
      {
        _searchFocused = matches[0].TruckId;
        await SelectRouteAsync(matches[0].TruckId, null);
      }
    }
  }

  private async Task LoadRouteAsync(bool fit, bool force = false)
  {
    if (
      _map is null
      || _disposed
      || _routeLoading
      || _previewRequest is not null
      || _recalculatingFuel
      || (!_activeTruckId.HasValue && !_activeDispatchId.HasValue)
    )
      return;
    var url = _activeDispatchId.HasValue
      ? $"api/dispatch/{_activeDispatchId}/planning/automatic"
      : $"api/fleet/trucks/{_activeTruckId}/planning";
    if (
      !force
      && _routeValidationUrl == url
      && Clock.GetUtcNow() < _routeValidationRetryAt
    )
      return;
    using var request = CancellationTokenSource.CreateLinkedTokenSource(
      _lifetime.Token
    );
    _routeRequest = request;
    _routeLoading = true;
    _etaRefreshPending = true;
    if (RetainingRouteDisplay && DisplayRouteState?.Eta is { } retained)
      _retainedProgressUntil = retained
        .ValidUntil.ToUniversalTime()
        .AddMinutes(15);
    await InvokeAsync(StateHasChanged);
    try
    {
      await SendMapStopEtasAsync();
      if (
        _disposed
        || request.IsCancellationRequested
        || !ReferenceEquals(_routeRequest, request)
      )
        return;
      var result = await PlanningCache.RefreshAsync(url, request.Token, force);
      if (
        _disposed
        || request.IsCancellationRequested
        || !ReferenceEquals(_routeRequest, request)
      )
        return;
      if (!result.Success)
      {
        if (
          result.HttpStatusCode
          is HttpStatusCode.Unauthorized
            or HttpStatusCode.Forbidden
        )
        {
          await ClearSelectionAsync();
          return;
        }
        if (result.HttpStatusCode is HttpStatusCode.BadRequest)
        {
          RouteError = string.IsNullOrWhiteSpace(result.ErrorMessage)
            ? "Review the load assignment and route inputs."
            : RouteMessageDisplay.Concise(result.ErrorMessage);
          _routeValidationUrl = url;
          _routeValidationRetryAt = Clock.GetUtcNow().AddMinutes(1);
        }
        else if (!RetainingRouteDisplay)
          RouteError =
            "Route is temporarily unavailable. Retrying automatically.";
        return;
      }
      _routeValidationUrl = null;
      _etaRefreshPending = false;
      var previousDispatchId = SelectedDispatchId;
      var previousExecutionLegId = SelectedExecutionLegId;
      var previousAssignmentRevision = SelectedAssignmentRevision;
      _planningDispatchId = result.Response?.DispatchId;
      _planningExecutionLegId =
        result.Response?.ExecutionLegId
        ?? result.Response?.State?.Plan?.ExecutionLegId;
      _planningAssignmentRevision =
        result.Response?.State?.Plan?.AssignmentRevision
        ?? result.Response?.AssignmentRevision
        ?? 0;
      SetRouteState(
        result.Response?.State,
        result.Response?.TruckId,
        result.Response?.DispatchId,
        result.Response?.ExecutionLegId,
        result.Response?.AssignmentRevision ?? 0
      );
      _hos = SelectedHos(result.Response?.Hos);
      RouteError = result.Response?.Message;
      var identityChanged =
        previousDispatchId != SelectedDispatchId
        || previousExecutionLegId != SelectedExecutionLegId
        || previousAssignmentRevision != SelectedAssignmentRevision;
      await SendMapRouteAsync(fit);
      if (
        _disposed
        || request.IsCancellationRequested
        || !ReferenceEquals(_routeRequest, request)
      )
        return;
      if (identityChanged)
        await Task.WhenAll(RefreshNextLoadsAsync(), LoadDispatchDetailsAsync());
    }
    catch (Exception ex) when (IsLoadError(ex))
    {
      if (
        !_disposed
        && !request.IsCancellationRequested
        && !RetainingRouteDisplay
      )
        RouteError = "Route could not be displayed. Retrying automatically.";
    }
    finally
    {
      if (ReferenceEquals(_routeRequest, request))
      {
        _routeRequest = null;
        _routeLoading = false;
      }
      if (!_disposed)
        await InvokeAsync(StateHasChanged);
    }
  }

  private bool _fuelRevalidationPending;

  private async Task OnFuelBusyChanged(bool busy)
  {
    _recalculatingFuel = busy;
    if (busy)
    {
      _fuelRevalidationPending = false;
      _routeRequest?.Cancel();
      _routeRequest = null;
      _routeLoading = false;
    }
    else if (_fuelRevalidationPending)
    {
      _fuelRevalidationPending = false;
      await LoadRouteAsync(false, force: true);
    }
  }

  private Task OnFuelFailed()
  {
    if (!_disposed && _routeState?.Plan is not null)
      _fuelRevalidationPending = true;
    return Task.CompletedTask;
  }

  private async Task OnFuelRecalculated(AutomaticPlanningResult result)
  {
    if (
      _disposed
      || _map is null
      || _routeState?.Plan is not { } current
      || result.DispatchId != current.DispatchId
      || result.TruckId != current.TruckId
      || (result.ExecutionLegId ?? result.State?.Plan?.ExecutionLegId)
        != SelectedExecutionLegId
      || (result.State?.Plan?.AssignmentRevision ?? result.AssignmentRevision)
        != SelectedAssignmentRevision
    )
      return;
    _etaRefreshPending = false;
    SetRouteState(result.State);
    RouteError = null;
    var url = _activeDispatchId.HasValue
      ? $"api/dispatch/{_activeDispatchId}/planning/automatic"
      : $"api/fleet/trucks/{_activeTruckId}/planning";
    PlanningCache.Store(url, result);
    await SendMapRouteAsync(false);
  }

  private async Task OnDateChanged()
  {
    if (_disposed || _stations is null)
      return;
    var date = SelectedDate;
    _stations.Cancel();
    await _stations.LoadPricesAsync(date, () => UseIfta, _lifetime.Token);
    if (_disposed || date != SelectedDate)
      return;
    if (ShowFuelStations || _inspectorMode == MapInspectorMode.Fuel)
      await _stations.LoadAsync(date, () => UseIfta, _lifetime.Token);
    if (!_disposed)
      await InvokeAsync(StateHasChanged);
  }

  private async Task OnIftaToggleChanged()
  {
    await SaveMapPreferencesAsync();
    if (_map is not null && !_disposed)
      await _map.InvokeVoidAsync("setIfta", UseIfta);
  }

  private async Task OnFuelStationsToggleChanged()
  {
    await SaveMapPreferencesAsync();
    if (_map is not null && !_disposed)
      await _map.InvokeVoidAsync("setStationsVisible", ShowFuelStations);
    if (!ShowFuelStations && _inspectorMode != MapInspectorMode.Fuel)
    {
      _stations?.Cancel();
    }
    else if (_map is not null && _stations?.LoadedDate != SelectedDate)
      await OnDateChanged();
  }

  private async Task OnTrafficToggleChanged()
  {
    await SaveMapPreferencesAsync();
    if (_map is not null && !_disposed)
      await _map.InvokeVoidAsync("setTrafficVisible", ShowTraffic);
  }

  private static bool IsLoadError(Exception ex) =>
    ex
      is HttpRequestException
        or OperationCanceledException
        or JsonException
        or JSException;

  public async ValueTask DisposeAsync()
  {
    if (_disposed)
      return;
    _disposed = true;
    ResetFuelEditor();
    ResetInspectedLoad();
    _inspectedDetailsCache.Clear();
    _nextLoadsCache.Clear();
    _stations?.Dispose();
    try
    {
      await _lifetime.CancelAsync();
      if (_initializationTask is not null)
        await _initializationTask;
      if (_truckPollingTask is not null)
        await _truckPollingTask;
      if (_hosPollingTask is not null)
        await _hosPollingTask;
      _hosSnapshot.Clear();
    }
    finally
    {
      try
      {
        if (_session is not null)
          await _session.DisposeAsync();
      }
      catch (JSDisconnectedException) { }
      finally
      {
        await _visibility.DisposeAsync();
        _lifetime.Dispose();
      }
    }
    GC.SuppressFinalize(this);
  }

  // A plan is worth showing when it still holds - whether its prices belong
  // to an earlier pricing day or the truck's position could not be read.
  // Only a plan whose stops no longer match the work is hidden, because a
  // driver with no fuel stop at all is the worse answer.
  private static bool Usable(FuelPlan? fuel) =>
    fuel is { Stops.Count: > 0 }
    && (!fuel.NeedsRefresh || fuel.PricesOutOfDate || fuel.PositionUnverified);
}
