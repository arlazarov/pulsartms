using System.Net;
using Client.Models.DTO;
using Client.Models.DTO.Dispatch;
using Client.Models.DTO.Fleet;
using Client.Models.DTO.Planning;
using Client.Services;
using Client.Shared.Dispatch;
using Client.Shared.Search;
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;

namespace Client.Pages.Dispatch;

public partial class DispatchList : IDisposable, IAsyncDisposable
{
  [Inject]
  private PlanningDisplayCache PlanningCache { get; set; } = default!;

  [Inject]
  private IJSRuntime JS { get; set; } = default!;

  [Inject]
  private TimeProvider Clock { get; set; } = default!;
  private const string ViewStorageKey = "pulsartms.dispatch.view";
  private const string LegacyViewStorageKey = "amftms.dispatch.view";

  protected override async Task OnInitializedAsync()
  {
    try
    {
      var saved = await JS.InvokeAsync<string?>(
        "localStorage.getItem",
        ViewStorageKey
      );
      var migrate = saved is null;
      saved ??= await JS.InvokeAsync<string?>(
        "localStorage.getItem",
        LegacyViewStorageKey
      );
      if (int.TryParse(saved, out var view) && view is >= 0 and <= 2)
      {
        var module = await JS.InvokeAsync<IJSObjectReference>(
          "import",
          "./js/generated/dispatch/dispatch.js"
        );
        try
        {
          _view =
            view == 1 && await module.InvokeAsync<bool>("isMobile") ? 0 : view;
        }
        finally
        {
          await module.DisposeAsync();
        }
        if (migrate)
        {
          try
          {
            await JS.InvokeVoidAsync(
              "localStorage.setItem",
              ViewStorageKey,
              saved
            );
          }
          catch (JSException) { }
        }
      }
    }
    catch (JSException) { }
  }

  private async Task SelectViewAsync(int view)
  {
    _view = view;
    await LoadAsync(1);
    try
    {
      await JS.InvokeVoidAsync(
        "localStorage.setItem",
        ViewStorageKey,
        view.ToString()
      );
    }
    catch (JSException) { }
  }

  [Inject]
  private ApiService Api { get; set; } = default!;

  [SupplyParameterFromQuery]
  public Guid? TruckId { get; set; }
  private PaginatedListDTO<TruckDispatchBoardResponse>? _data;
  private string _search = "";
  private string? _loadedSearch;
  private readonly Dictionary<Guid, DispatchTruckStatus> _latestTelemetry = [];
  private readonly Dictionary<
    Guid,
    AutomaticPlanningResult
  > _planningSummaries = [];
  private readonly PageVisibility _visibility = new();
  private CancellationTokenSource? _planningRequest;
  private DateTime _lastPlanningRefresh;
  private bool _planningRefreshing;
  private bool _planningRefreshFailed;
  private List<SearchSuggestion> SearchOptions =>
    (_loadedSearch == _search ? _data?.Items ?? [] : [])
      .Where(x => !string.IsNullOrWhiteSpace(x.TruckNumber))
      .Select(x => new SearchSuggestion(
        x.TruckNumber,
        $"{x.DriverName} · {x.TrailerNumber}"
      ))
      .DistinctBy(x => x.Value)
      .ToList();
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
  private bool RefreshingEstimates =>
    _boardRefreshing
    || _boardRefreshFailed
    || _planningRefreshing
    || _planningRefreshFailed
    || _enrichmentRequest is not null
    || _enrichmentFailed;

  protected override Task OnAfterRenderAsync(bool firstRender) =>
    firstRender ? _visibility.StartAsync(JS) : Task.CompletedTask;

  private string TruckMotion(TruckDispatchBoardResponse truck) =>
    DispatchRigStatus.Resolve(
      truck.Speed,
      truck.EngineState,
      truck.Hos,
      Clock.GetUtcNow().UtcDateTime
    );

  private string TruckMotionLabel(TruckDispatchBoardResponse truck) =>
    TruckMotion(truck) switch
    {
      "moving" => $"Driving · {truck.Speed:0} mph",
      "sleeping" => "Sleeper Berth",
      "idling" => "Idle",
      "off" => "Engine off",
      _ => "Parked",
    };

  private DispatchBoardRequest BoardRequest(int page) =>
    new(
      page,
      _search,
      TruckId,
      _view,
      DateOnly.FromDateTime(Clock.GetLocalNow().DateTime),
      _showCompleted
    );

  private async Task SelectScopeAsync(bool completed)
  {
    if (_showCompleted == completed)
      return;
    _showCompleted = completed;
    _searchDelay?.Cancel();
    await LoadAsync(1);
  }

  private async Task<
    RequestResponseDTO<PaginatedListDTO<TruckDispatchBoardResponse>>
  > ReadBoardAsync(DispatchBoardRequest query, CancellationToken ct)
  {
    if (!query.Completed)
      return await Api.GetAsync<PaginatedListDTO<TruckDispatchBoardResponse>>(
        query.Url
          + "&includeHos=false&includeFinancials=false&includeEta=false",
        ct
      );
    var result = await Api.GetAsync<PaginatedListDTO<DispatchResponse>>(
      query.Url,
      ct
    );
    var page = result.Response;
    return new()
    {
      Success = result.Success,
      Errors = result.Errors,
      HttpStatusCode = result.HttpStatusCode,
      Response = page is null
        ? null
        : new()
        {
          Page = page.Page,
          PageSize = page.PageSize,
          TotalCount = page.TotalCount,
          TotalPages = page.TotalPages,
          HasPreviousPage = page.HasPreviousPage,
          HasNextPage = page.HasNextPage,
          Items = page
            .Items.Select(load => new TruckDispatchBoardResponse
            {
              Key = load.Id.ToString(),
              TruckId = load.TruckId,
              TruckNumber = load.TruckNumber,
              DriverName = load.DriverName,
              TrailerNumber = load.TrailerNumber,
              Dispatches = [load],
            })
            .ToList(),
        },
    };
  }

  protected override Task OnParametersSetAsync() => LoadAsync(1);

  private async Task OnSearchInput(string value)
  {
    _search = value;
    _request?.Cancel();
    _searchDelay?.Cancel();
    _searchDelay?.Dispose();
    var delay = CancellationTokenSource.CreateLinkedTokenSource(
      _lifetime.Token
    );
    _searchDelay = delay;
    try
    {
      await Task.Delay(TimeSpan.FromMilliseconds(300), Clock, delay.Token);
      await LoadAsync(1);
    }
    catch (OperationCanceledException) when (delay.IsCancellationRequested) { }
    finally
    {
      if (ReferenceEquals(_searchDelay, delay))
        _searchDelay = null;
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

  private async Task StopChangedAsync(DispatchResponse load)
  {
    InvalidatePlanning(load);
    await LoadAsync(_page);
    foreach (
      var changed in _data
        ?.Items.SelectMany(truck => truck.Dispatches)
        .Where(item => item.Id == load.Id) ?? []
    )
      InvalidatePlanning(changed, invalidateSummary: false);
  }

  private void InvalidatePlanning(
    DispatchResponse load,
    bool invalidateSummary = true
  )
  {
    foreach (
      var truck in load
        .Stops.Select(stop => stop.TruckId)
        .Append(load.TruckId)
        .Where(id => id.HasValue)
        .Select(id => id!.Value)
        .DefaultIfEmpty(Guid.Empty)
        .Distinct()
    )
    {
      PlanningCache.Invalidate(truck, load.Id);
      if (invalidateSummary)
        _planningSummaries.Remove(truck);
    }
  }

  private async Task LoadAsync(int page)
  {
    var version = ++_boardVersion;
    _enrichmentRequest?.Cancel();
    var query = BoardRequest(page);
    if (_loadedQuery != query)
    {
      _data = null;
      _boardRefreshFailed = false;
      _planningSummaries.Clear();
      _latestTelemetry.Clear();
    }
    _planningRequest?.Cancel();
    _planningRequest = null;
    _planningRefreshing = false;
    _planningRefreshFailed = false;
    _lastPlanningRefresh = default;
    _request?.Cancel();
    using var request = CancellationTokenSource.CreateLinkedTokenSource(
      _lifetime.Token
    );
    _request = request;
    _loading = true;
    _boardRefreshing = _data is not null;
    _error = null;
    await InvokeAsync(StateHasChanged);
    try
    {
      var result = await ReadBoardAsync(query, request.Token);
      if (
        _disposed
        || request.IsCancellationRequested
        || version != _boardVersion
      )
        return;
      _page = page;
      if (result.Success)
      {
        _data = query.Completed
          ? result.Response
          : MergeBoardForecasts(result.Response);
        _loadedQuery = query;
        _boardRefreshFailed = false;
        _loadedSearch = query.Search;
        _lastBoardRefresh = Clock.GetUtcNow().UtcDateTime;
        if (!query.Completed)
        {
          ApplyTelemetry(_latestTelemetry.Values);
          ApplyHos();
          _telemetryPollingTask ??= PollTelemetryAsync(_lifetime.Token);
          _hosPollingTask ??= HosRefreshLoop.RunAsync(
            RefreshHosAsync,
            _lifetime.Token,
            Clock,
            _visibility
          );
          _ = EnrichBoardAsync(query, version, _lifetime.Token);
          _ = RefreshPlanningAsync(query, version, _lifetime.Token);
        }
      }
      else
      {
        _error = result.ErrorMessage;
        if (
          result.HttpStatusCode
          is HttpStatusCode.Unauthorized
            or HttpStatusCode.Forbidden
        )
          _data = null;
        _boardRefreshFailed = _data is not null;
      }
      _boardRefreshing = false;
      _loading = false;
    }
    finally
    {
      if (ReferenceEquals(_request, request))
        _request = null;
    }
  }

  private void OnForecastChanged(
    TruckDispatchBoardResponse owner,
    Guid currentId,
    DispatchEta forecast
  )
  {
    if (
      _disposed
      || _showCompleted
      || _view != 0
      || owner.TruckId is not { } truckId
      || truckId == Guid.Empty
      || forecast.ValidUntil.ToUniversalTime() <= Clock.GetUtcNow().UtcDateTime
    )
      return;
    var truck = _data?.Items.FirstOrDefault(item =>
      item.Key == owner.Key
      && item.TruckId == truckId
      && item.Dispatches.FirstOrDefault()?.Id == currentId
    );
    if (truck is null)
      return;
    var estimates = forecast
      .Stops.Where(stop => stop.DispatchId != Guid.Empty)
      .GroupBy(stop => stop.DispatchId)
      .ToDictionary(group => group.Key, group => group.ToArray());
    foreach (var load in truck.Dispatches)
    {
      if (
        load.Id == Guid.Empty
        || load.TruckId is { } assigned && assigned != truckId
        || load.Eta is { } previous
          && previous.CalculatedAt >= forecast.CalculatedAt
      )
        continue;
      var reason = forecast.PendingDispatches.GetValueOrDefault(load.Id);
      if (!estimates.TryGetValue(load.Id, out var stops) && reason is null)
        continue;
      load.Eta = forecast with
      {
        Stops = stops ?? [],
        UnavailableReason = reason,
        RouteUpdatePending = forecast.RouteUpdatePending || reason is not null,
        PendingDispatches = new Dictionary<Guid, string>(),
      };
    }
  }

  private PaginatedListDTO<TruckDispatchBoardResponse>? MergeBoardForecasts(
    PaginatedListDTO<TruckDispatchBoardResponse>? incoming
  )
  {
    if (incoming is null || _data is null)
      return incoming;
    foreach (var truck in incoming.Items)
    {
      if (truck.TruckId is not { } truckId || truckId == Guid.Empty)
        continue;
      var previous = _data.Items.FirstOrDefault(item =>
        item.Key == truck.Key
        && item.TruckId == truckId
        && item.Dispatches.FirstOrDefault()?.Id
          == truck.Dispatches.FirstOrDefault()?.Id
      );
      if (previous is null)
        continue;
      if (truck.DriverName == previous.DriverName)
      {
        truck.Hos ??= previous.Hos;
        truck.CurrentCycle ??= previous.CurrentCycle;
      }
      foreach (var load in truck.Dispatches)
      {
        var priorLoad = previous.Dispatches.FirstOrDefault(item =>
          item.Id == load.Id
        );
        if (priorLoad is null || !SameInputs(load, priorLoad))
          continue;
        CopyFinancials(priorLoad, load);
        if (load.TruckId is { } assigned && assigned != truckId)
          continue;
        var retained = priorLoad.Eta;
        if (
          retained is null
          || load.Eta is { Stops.Count: 0, RouteUpdatePending: false }
        )
          continue;
        if (load.Eta is null)
          load.Eta = retained with { RouteUpdatePending = true };
        else if (retained.CalculatedAt > load.Eta.CalculatedAt)
          load.Eta = retained;
      }
    }
    return incoming;
  }

  private bool ApplyTelemetry(IEnumerable<DispatchTruckStatus> fleet)
  {
    if (_data is null || _showCompleted)
      return false;
    var telemetry = fleet.ToDictionary(x => x.TruckId);
    var changed = false;
    foreach (var truck in _data.Items)
    {
      if (
        !truck.TruckId.HasValue
        || !telemetry.TryGetValue(truck.TruckId.Value, out var location)
      )
        continue;
      _latestTelemetry[truck.TruckId.Value] = location;
      changed |=
        truck.Speed != location.Speed
        || truck.EngineState != location.EngineState
        || truck.TrailerNumber != location.TrailerNumber;
      truck.Speed = location.Speed;
      truck.EngineState = location.EngineState;
      truck.TrailerNumber = location.TrailerNumber;
    }
    return changed;
  }

  private Task PollTelemetryAsync(CancellationToken cancellationToken) =>
    RefreshLoop.RunAsync(
      RefreshBoardAsync,
      _ => Task.CompletedTask,
      TimeSpan.FromSeconds(10),
      cancellationToken,
      Clock,
      _visibility
    );

  internal async Task RefreshBoardAsync(CancellationToken token)
  {
    if (_disposed || !_visibility.IsVisible || _showCompleted || _data is null)
      return;
    var version = _boardVersion;
    if (
      !_loading
      && _searchDelay is null
      && Clock.GetUtcNow().UtcDateTime - _lastBoardRefresh
        >= TimeSpan.FromMinutes(1)
    )
    {
      _boardRefreshing = _loadedQuery == BoardRequest(_page);
      await InvokeAsync(StateHasChanged);
    }
    if (
      _disposed
      || !_visibility.IsVisible
      || _showCompleted
      || version != _boardVersion
      || _data is null
    )
      return;
    var ids = _data
      .Items.Where(truck => truck.TruckId.HasValue)
      .Select(truck => truck.TruckId!.Value)
      .Distinct()
      .ToArray();
    var telemetryUrl =
      "api/dispatch/board/telemetry?"
      + string.Join("&", ids.Select(id => $"truckIds={id}"));
    var fleet =
      ids.Length == 0
        ? null
        : await Api.GetAsync<List<DispatchTruckStatus>>(telemetryUrl, token);
    if (
      _disposed
      || !_visibility.IsVisible
      || _showCompleted
      || version != _boardVersion
    )
      return;
    var changed =
      fleet is { Success: true, Response: not null }
      && ApplyTelemetry(fleet.Response);
    if (
      !_loading
      && _searchDelay is null
      && Clock.GetUtcNow().UtcDateTime - _lastBoardRefresh
        >= TimeSpan.FromMinutes(1)
    )
    {
      var query = BoardRequest(_page);
      _lastBoardRefresh = Clock.GetUtcNow().UtcDateTime;
      if (!_boardRefreshing)
      {
        _boardRefreshing = _loadedQuery == query && _data is not null;
        await InvokeAsync(StateHasChanged);
      }
      var board = await ReadBoardAsync(query, token);
      if (_disposed)
        return;
      if (
        !_loading
        && _searchDelay is null
        && version == _boardVersion
        && query == BoardRequest(_page)
      )
      {
        _boardRefreshing = false;
        changed = true;
        if (board.Success && board.Response is not null)
        {
          _lastBoardRefresh = Clock.GetUtcNow().UtcDateTime;
          _data = MergeBoardForecasts(board.Response);
          _loadedQuery = query;
          _boardRefreshFailed = false;
          ++_boardVersion;
          _planningRequest?.Cancel();
          _planningRequest = null;
          _planningRefreshing = false;
          PrunePageState();
          ApplyTelemetry(_latestTelemetry.Values);
          ApplyHos();
          _ = EnrichBoardAsync(query, _boardVersion, token);
        }
        else
        {
          if (
            board.HttpStatusCode
            is HttpStatusCode.Unauthorized
              or HttpStatusCode.Forbidden
          )
          {
            _data = null;
            _error = board.ErrorMessage;
          }
          _boardRefreshFailed = _loadedQuery == query && _data is not null;
        }
      }
    }
    if (changed && !_disposed)
      await InvokeAsync(StateHasChanged);
    await RefreshPlanningAsync(BoardRequest(_page), _boardVersion, token);
  }

  private async Task RefreshPlanningAsync(
    DispatchBoardRequest query,
    int version,
    CancellationToken ct
  )
  {
    if (
      _disposed
      || !_visibility.IsVisible
      || query.Completed
      || query.View != 0
      || _data is null
      || _planningRequest is not null
      || !_data.Items.Any(truck =>
        truck.TruckId.HasValue && truck.Dispatches.Count > 0
      )
    )
      return;
    var interval = _data
      .Items.Where(truck =>
        truck.TruckId.HasValue && truck.Dispatches.Count > 0
      )
      .Any(truck =>
        !_planningSummaries.TryGetValue(truck.TruckId!.Value, out var summary)
        || summary.State?.Plan is null
      )
      ? TimeSpan.FromSeconds(10)
      : TimeSpan.FromMinutes(1);
    if (Clock.GetUtcNow().UtcDateTime - _lastPlanningRefresh < interval)
      return;
    using var request = CancellationTokenSource.CreateLinkedTokenSource(ct);
    _planningRequest = request;
    _planningRefreshing = true;
    try
    {
      await InvokeAsync(StateHasChanged);
      if (
        _disposed
        || request.IsCancellationRequested
        || version != _boardVersion
      )
        return;
      var response = await Api.PostAsync<object, List<AutomaticPlanningResult>>(
        "api/dispatch/board/planning",
        new
        {
          query.Page,
          query.Search,
          query.TruckId,
          query.Date,
        },
        request.Token
      );
      if (
        _disposed
        || request.IsCancellationRequested
        || version != _boardVersion
        || query != BoardRequest(_page)
      )
        return;
      _lastPlanningRefresh = Clock.GetUtcNow().UtcDateTime;
      if (response.Success && response.Response is not null)
      {
        _planningRefreshFailed = false;
        foreach (var truck in _data?.Items ?? [])
        {
          if (truck.TruckId is not { } truckId)
            continue;
          var summary = response.Response.FirstOrDefault(result =>
            result.TruckId == truckId
            && result.DispatchId == truck.Dispatches.FirstOrDefault()?.Id
          );
          if (summary is null)
          {
            _planningSummaries.Remove(truckId);
            continue;
          }
          summary = PlanningDisplayCache.ForDisplay(
            summary,
            Clock.GetUtcNow().UtcDateTime
          )!;
          _planningSummaries[truckId] = summary;
          if (
            summary.State?.Eta is { } eta
            && summary.DispatchId is { } current
          )
            OnForecastChanged(truck, current, eta);
        }
      }
      else if (
        response.HttpStatusCode
        is HttpStatusCode.Unauthorized
          or HttpStatusCode.Forbidden
      )
      {
        _planningSummaries.Clear();
        _data = null;
        _error = response.ErrorMessage;
      }
      else
        _planningRefreshFailed = true;
    }
    catch (OperationCanceledException) when (request.IsCancellationRequested)
    { }
    finally
    {
      if (ReferenceEquals(_planningRequest, request))
      {
        _planningRequest = null;
        _planningRefreshing = false;
        if (!_disposed)
          await InvokeAsync(StateHasChanged);
      }
    }
  }

  private void PrunePageState()
  {
    var ids = (_data?.Items ?? [])
      .Where(truck => truck.TruckId.HasValue)
      .Select(truck => truck.TruckId!.Value)
      .ToHashSet();
    foreach (
      var id in _planningSummaries.Keys.Where(id => !ids.Contains(id)).ToArray()
    )
      _planningSummaries.Remove(id);
    foreach (
      var id in _latestTelemetry.Keys.Where(id => !ids.Contains(id)).ToArray()
    )
      _latestTelemetry.Remove(id);
  }

  public void Dispose()
  {
    if (_disposed)
      return;
    _disposed = true;
    _request?.Cancel();
    _planningRequest?.Cancel();
    _enrichmentRequest?.Cancel();
    _searchDelay?.Cancel();
    _searchDelay?.Dispose();
    _lifetime.Cancel();
    _lifetime.Dispose();
    GC.SuppressFinalize(this);
  }

  public async ValueTask DisposeAsync()
  {
    Dispose();
    await _visibility.DisposeAsync();
  }

  private bool IsCurrent(DispatchResponse? load) =>
    !_showCompleted
    && load is not null
    && !load.Completed
    && (
      load.Status == "in_transit"
      || (load.Stops.FirstOrDefault()?.ScheduledDate ?? load.ShipDate)
        <= DateOnly.FromDateTime(Clock.GetLocalNow().DateTime)
    );

  private string LoadPhase(
    TruckDispatchBoardResponse truck,
    DispatchResponse load
  )
  {
    if (_showCompleted || load.Completed)
      return "Completed";
    var index = truck.Dispatches.TakeWhile(item => item.Id != load.Id).Count();
    var hasCurrent = IsCurrent(truck.Dispatches.FirstOrDefault());
    if (index == 0 && hasCurrent)
      return "Current";
    return index + (hasCurrent ? 0 : 1) == 1 ? "Next" : "Upcoming";
  }

  private DispatchCardPlanningSummary CardPlanningSummary(
    TruckDispatchBoardResponse truck,
    DispatchResponse load
  ) =>
    _showCompleted || truck.TruckId is not { } truckId
      ? default
      : DispatchCardPlanningSummary.From(
        _planningSummaries.GetValueOrDefault(truckId),
        truckId,
        truck.Dispatches.FirstOrDefault()?.Id,
        load.Id
      );
}
