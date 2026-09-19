using Client.Models.DTO.Dispatch;
using Client.Models.DTO.Fleet;
using Client.Shared.Dispatch;
using Client.Models.DTO;
using Client.Models.DTO.Planning;
using Client.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;

namespace Client.Pages.Dispatch;

public partial class DispatchList : IDisposable
{
  [Inject] private PlanningDisplayCache PlanningCache { get; set; } = default!;
  [Inject] private IJSRuntime JS { get; set; } = default!;
  [Inject] private TimeProvider Clock { get; set; } = default!;
  [Inject] private IPageVisibility Visibility { get; set; } = default!;
  private const string ViewStorageKey = "amftms.dispatch.view";

  protected override async Task OnInitializedAsync()
  {
    _ = PlanningCache.PreloadAsync();
    try
    {
      var saved = await JS.InvokeAsync<string?>("localStorage.getItem", ViewStorageKey);
      if (int.TryParse(saved, out var view) && view is >= 0 and <= 2)
      {
        var module = await JS.InvokeAsync<IJSObjectReference>("import", "./js/generated/dispatch/dispatch.js");
        try { _view = view == 1 && await module.InvokeAsync<bool>("isMobile") ? 0 : view; }
        finally { await module.DisposeAsync(); }
      }
    }
    catch (JSException) { }
  }

  private async Task SelectViewAsync(int view)
  {
    _view = view;
    await LoadAsync(1);
    try { await JS.InvokeVoidAsync("localStorage.setItem", ViewStorageKey, view.ToString()); }
    catch (JSException) { }
  }
  [Inject] private ApiService Api { get; set; } = default!;
  [SupplyParameterFromQuery] public Guid? TruckId { get; set; }
  private PaginatedListDTO<TruckDispatchBoardResponse>? _data;
  private string _search = "";
  private string? _loadedSearch;
  private FleetLocationsMapDto? _latestTelemetry;
  private List<Client.Shared.Search.SearchSuggestion> SearchOptions => (_loadedSearch == _search ? _data?.Items ?? [] : [])
    .Where(x => !string.IsNullOrWhiteSpace(x.TruckNumber))
    .Select(x => new Client.Shared.Search.SearchSuggestion(x.TruckNumber, $"{x.DriverName} · {x.TrailerNumber}"))
    .DistinctBy(x => x.Value).ToList();
  private string? _error;
  private int _page = 1;
  private int _view;
  private bool _showCompleted;
  private bool _loading;
  private bool _disposed;
  private CancellationTokenSource? _request;
  private CancellationTokenSource? _searchDelay;
  private readonly CancellationTokenSource _lifetime = new();
  private Task? _telemetryPollingTask;
  private DateTime _lastBoardRefresh;
  private int _boardVersion;
  private DispatchBoardRequest? _loadedQuery;
  private bool _boardRefreshing;
  private bool _boardRefreshFailed;
  private bool RefreshingEstimates => _boardRefreshing || _boardRefreshFailed;
  private string TruckMotion(TruckDispatchBoardResponse truck) =>
    DispatchRigStatus.Resolve(truck.Speed, truck.EngineState, truck.Hos, Clock.GetUtcNow().UtcDateTime);
  private string TruckMotionLabel(TruckDispatchBoardResponse truck) => TruckMotion(truck) switch
  {
    "moving" => $"Driving · {truck.Speed:0} mph", "sleeping" => "Sleeper Berth",
    "idling" => "Idle", "off" => "Engine off", _ => "Parked"
  };
  private DispatchBoardRequest BoardRequest(int page) => new(page, _search, TruckId, _view, DateOnly.FromDateTime(Clock.GetLocalNow().DateTime), _showCompleted);

  private async Task SelectScopeAsync(bool completed)
  {
    if (_showCompleted == completed) return;
    _showCompleted = completed;
    _searchDelay?.Cancel();
    await LoadAsync(1);
  }

  private async Task<RequestResponseDTO<PaginatedListDTO<TruckDispatchBoardResponse>>> ReadBoardAsync(DispatchBoardRequest query, CancellationToken ct)
  {
    if (!query.Completed) return await Api.GetAsync<PaginatedListDTO<TruckDispatchBoardResponse>>(query.Url, ct);
    var result = await Api.GetAsync<PaginatedListDTO<DispatchResponse>>(query.Url, ct);
    var page = result.Response;
    return new() { Success = result.Success, Errors = result.Errors, HttpStatusCode = result.HttpStatusCode,
      Response = page is null ? null : new() {
        Page = page.Page, PageSize = page.PageSize, TotalCount = page.TotalCount, TotalPages = page.TotalPages,
        HasPreviousPage = page.HasPreviousPage, HasNextPage = page.HasNextPage,
        Items = page.Items.Select(load => new TruckDispatchBoardResponse {
          Key = load.Id.ToString(), TruckId = load.TruckId, TruckNumber = load.TruckNumber,
          DriverName = load.DriverName, TrailerNumber = load.TrailerNumber, Dispatches = [load]
        }).ToList()
      }
    };
  }

  protected override Task OnParametersSetAsync() => LoadAsync(1);

  private async Task OnSearchInput(string value)
  {
    _search = value;
    _request?.Cancel();
    _searchDelay?.Cancel();
    _searchDelay?.Dispose();
    var delay = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
    _searchDelay = delay;
    try
    {
      await Task.Delay(TimeSpan.FromMilliseconds(300), Clock, delay.Token);
      await LoadAsync(1);
    }
    catch (OperationCanceledException) when (delay.IsCancellationRequested) { }
    finally
    {
      if (ReferenceEquals(_searchDelay, delay)) _searchDelay = null;
      delay.Dispose();
    }
  }

  private async Task ChooseSearchAsync(string value)
  {
    _search = value;
    _searchDelay?.Cancel();
    await LoadAsync(1);
  }

  private Task SubmitSearchAsync() => ChooseSearchAsync(_search);

  private async Task LoadAsync(int page)
  {
    var version = ++_boardVersion;
    var query = BoardRequest(page);
    if (_loadedQuery != query) { _data = null; _boardRefreshFailed = false; }
    _request?.Cancel();
    using var request = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
    _request = request;
    _loading = true;
    _boardRefreshing = _data is not null;
    _error = null;
    await InvokeAsync(StateHasChanged);
    try
    {
      var result = await ReadBoardAsync(query, request.Token);
      if (_disposed || request.IsCancellationRequested || version != _boardVersion) return;
      _page = page;
      if (result.Success)
      {
        _data = query.Completed ? result.Response : MergeBoardForecasts(result.Response);
        _loadedQuery = query;
        _boardRefreshFailed = false;
        _loadedSearch = query.Search;
        _lastBoardRefresh = Clock.GetUtcNow().UtcDateTime;
        if (!query.Completed)
        {
          if (_latestTelemetry is not null) ApplyTelemetry(_latestTelemetry);
          _telemetryPollingTask ??= PollTelemetryAsync(_lifetime.Token);
        }
      }
      else
      {
        _error = result.ErrorMessage;
        if (result.HttpStatusCode is System.Net.HttpStatusCode.Unauthorized or System.Net.HttpStatusCode.Forbidden)
          _data = null;
        _boardRefreshFailed = _data is not null;
      }
      _boardRefreshing = false;
      _loading = false;
    }
    finally { if (ReferenceEquals(_request, request)) _request = null; }
  }

  private void OnForecastChanged(TruckDispatchBoardResponse owner, Guid currentId, DispatchEta forecast)
  {
    if (_disposed || _showCompleted || _view != 0 || owner.TruckId is not { } truckId || truckId == Guid.Empty
      || forecast.ValidUntil.ToUniversalTime() <= Clock.GetUtcNow().UtcDateTime) return;
    var truck = _data?.Items.FirstOrDefault(item => item.Key == owner.Key && item.TruckId == truckId
      && item.Dispatches.FirstOrDefault()?.Id == currentId);
    if (truck is null) return;
    var estimates = forecast.Stops.Where(stop => stop.DispatchId != Guid.Empty).GroupBy(stop => stop.DispatchId)
      .ToDictionary(group => group.Key, group => group.ToArray());
    foreach (var load in truck.Dispatches)
    {
      if (load.Id == Guid.Empty || load.TruckId is { } assigned && assigned != truckId
        || load.Eta is { } previous && previous.CalculatedAt >= forecast.CalculatedAt) continue;
      var reason = forecast.PendingDispatches.GetValueOrDefault(load.Id);
      if (!estimates.TryGetValue(load.Id, out var stops) && reason is null) continue;
      load.Eta = forecast with { Stops = stops ?? [], UnavailableReason = reason,
        RouteUpdatePending = forecast.RouteUpdatePending || reason is not null,
        PendingDispatches = new Dictionary<Guid, string>() };
    }
  }

  private PaginatedListDTO<TruckDispatchBoardResponse>? MergeBoardForecasts(PaginatedListDTO<TruckDispatchBoardResponse>? incoming)
  {
    if (incoming is null || _data is null) return incoming;
    foreach (var truck in incoming.Items)
    {
      if (truck.TruckId is not { } truckId || truckId == Guid.Empty) continue;
      var previous = _data.Items.FirstOrDefault(item => item.Key == truck.Key && item.TruckId == truckId
        && item.Dispatches.FirstOrDefault()?.Id == truck.Dispatches.FirstOrDefault()?.Id);
      if (previous is null) continue;
      foreach (var load in truck.Dispatches)
      {
        if (load.TruckId is { } assigned && assigned != truckId) continue;
        var retained = previous.Dispatches.FirstOrDefault(item => item.Id == load.Id)?.Eta;
        if (retained is null || load.Eta is { Stops.Count: 0, RouteUpdatePending: false }) continue;
        if (load.Eta is null) load.Eta = retained with { RouteUpdatePending = true };
        else if (retained.CalculatedAt > load.Eta.CalculatedAt) load.Eta = retained;
      }
    }
    return incoming;
  }

  private void ApplyTelemetry(FleetLocationsMapDto fleet)
  {
    _latestTelemetry = fleet;
    if (_data is null || _showCompleted) return;
    var telemetry = fleet.Trucks.ToDictionary(x => x.TruckId);
    foreach (var truck in _data.Items)
    {
      if (!truck.TruckId.HasValue || !telemetry.TryGetValue(truck.TruckId.Value, out var location)) continue;
      truck.Speed = location.Speed;
      truck.EngineState = location.EngineState;
      truck.TrailerNumber = location.TrailerNumber;
    }
  }

  // The board shows current positions only; location history stays on the map.
  // wait= holds the poll on the server until telemetry changes, so quiet fleets cost one request per wait.
  internal const string TelemetryUrl = "api/fleet/locations?points=false&wait=25";

  private async Task PollTelemetryAsync(CancellationToken cancellationToken)
  {
    using var timer = new PeriodicTimer(TimeSpan.FromSeconds(10), Clock);
    using var pace = Visibility.Pace(timer, TimeSpan.FromSeconds(10));
    try
    {
      var initial = await Api.GetAsync<FleetLocationsMapDto>(TelemetryUrl, cancellationToken);
      if (_disposed) return;
      if (initial.Success && initial.Response is not null) ApplyTelemetry(initial.Response);
      await InvokeAsync(StateHasChanged);
      while (await timer.WaitForNextTickAsync(cancellationToken))
      {
        if (_showCompleted) continue;
        var fleet = await Api.GetAsync<FleetLocationsMapDto>(TelemetryUrl, cancellationToken);
        if (_disposed) return;
        if (_showCompleted) continue;
        if (!_loading && _searchDelay is null && Clock.GetUtcNow().UtcDateTime - _lastBoardRefresh >= TimeSpan.FromMinutes(1))
        {
          var query = BoardRequest(_page);
          var version = _boardVersion;
          _lastBoardRefresh = Clock.GetUtcNow().UtcDateTime;
          _boardRefreshing = _loadedQuery == query && _data is not null;
          await InvokeAsync(StateHasChanged);
          var board = await ReadBoardAsync(query, cancellationToken);
          if (_disposed) return;
          if (!_loading && _searchDelay is null && version == _boardVersion && query == BoardRequest(_page))
          {
            _boardRefreshing = false;
            if (board.Success && board.Response is not null)
            { _data = MergeBoardForecasts(board.Response); _loadedQuery = query; _boardRefreshFailed = false; }
            else
            {
              if (board.HttpStatusCode is System.Net.HttpStatusCode.Unauthorized or System.Net.HttpStatusCode.Forbidden)
              { _data = null; _error = board.ErrorMessage; }
              _boardRefreshFailed = _loadedQuery == query && _data is not null;
            }
          }
        }
        if (fleet.Success && fleet.Response is not null) ApplyTelemetry(fleet.Response);
        await InvokeAsync(StateHasChanged);
      }
    }
    catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
  }

  public void Dispose()
  {
    _disposed = true;
    _request?.Cancel();
    _searchDelay?.Cancel();
    _searchDelay?.Dispose();
    _lifetime.Cancel();
    _lifetime.Dispose();
    GC.SuppressFinalize(this);
  }
    private bool IsCurrent(DispatchResponse? load) => !_showCompleted && load is not null && !DispatchBoardRow.IsCompleted(load) &&
        (load.Status == "in_transit" || (load.Stops.FirstOrDefault()?.ScheduledDate ?? load.ShipDate) <= DateOnly.FromDateTime(Clock.GetLocalNow().DateTime));

    private string LoadPhase(TruckDispatchBoardResponse truck, DispatchResponse load)
    {
        if (_showCompleted || DispatchBoardRow.IsCompleted(load)) return "Completed";
        var index = truck.Dispatches.TakeWhile(item => item.Id != load.Id).Count();
        var hasCurrent = IsCurrent(truck.Dispatches.FirstOrDefault());
        if (index == 0 && hasCurrent) return "Current";
        return index + (hasCurrent ? 0 : 1) == 1 ? "Next" : "Upcoming";
    }

    private DispatchCardPlanningSummary CardPlanningSummary(TruckDispatchBoardResponse truck, DispatchResponse load) =>
        _showCompleted || truck.TruckId is not { } truckId ? default : DispatchCardPlanningSummary.From(
            PlanningCache.Get($"api/fleet/trucks/{truckId}/planning"), truckId,
            truck.Dispatches.FirstOrDefault()?.Id, load.Id);

    private Task RefreshCardSummaries() => _disposed || _showCompleted || _view != 0
        ? Task.CompletedTask : InvokeAsync(StateHasChanged);
}
