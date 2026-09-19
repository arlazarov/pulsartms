using Client.Models.DTO.Dispatch;
using Client.Models.DTO.Planning;
using Client.Services;
using Microsoft.AspNetCore.Components;

namespace Client.Pages.Dispatch;

public partial class DispatchPlanning : IDisposable
{
    [Inject] private PlanningDisplayCache PlanningCache { get; set; } = default!;
    [Inject] private TimeProvider Clock { get; set; } = default!;
    [Inject] private IPageVisibility Visibility { get; set; } = default!;
    [Parameter] public DispatchResponse? Load { get; set; }
    [Parameter] public Guid? TruckId { get; set; }
    [Parameter] public DriverHosClocks? Hos { get; set; }
    [Parameter] public DriverCycleSnapshot? CurrentCycle { get; set; }
    [Parameter] public bool Compact { get; set; }
    [Parameter] public bool BoardHeader { get; set; }
    [Parameter] public string? MotionState { get; set; }
    [Parameter] public string? MotionLabel { get; set; }
    [Parameter] public decimal Speed { get; set; }
    [Parameter] public bool Refreshing { get; set; }
    [Parameter] public EventCallback<DispatchEta> ForecastChanged { get; set; }
    [Parameter] public EventCallback DisplayChanged { get; set; }
    private AutomaticPlanningResult? _result;
    private AutomaticPlanningResult? _retainedResult;
    private string? _error;
    private string? _url;
    private Guid? _loadId;
    private readonly ArrivalDisplayMemory _arrivalMemory = new();
    private bool _loading;
    private bool RefreshingEstimate => Refreshing || _loading || _error is not null;
    private bool ShowRefreshError => _error is not null
        && _arrivalMemory.Display(_result?.State?.Eta, Clock.GetUtcNow().UtcDateTime, RefreshingEstimate)?.Stops.Count is not > 0;
    private int _readVersion;
    private DateTime _lastRefresh;
    private bool _disposed;
    private CancellationTokenSource? _request;
    private PlanStop? ArrivalStop
    {
        get
        {
            var planned = _result?.State?.Plan is { } plan
                ? plan.Stops.FirstOrDefault(stop => stop.Id == plan.Tracking.NextStopId)
                : _arrivalMemory.DispatchId == _result?.DispatchId ? _arrivalMemory.Stop : null;
            if (planned is null || Load?.Id != _result?.DispatchId
                || Load?.Stops.FirstOrDefault(stop => stop.Id == planned.Id) is not { } current) return planned;
            return planned with
            {
                Address = string.Join(", ", new[] { current.Address, current.City, current.Province, current.ZipCode, current.Country }
                    .Where(part => !string.IsNullOrWhiteSpace(part))),
                Point = new((double)(current.Latitude ?? 0), (double)(current.Longitude ?? 0)),
                Sequence = current.Sequence, Job = current.Job,
                ScheduledDate = current.ScheduledDate, ScheduledTime = current.ScheduledTime,
                ScheduledDate2 = current.ScheduledDate2, ScheduledTime2 = current.ScheduledTime2
            };
        }
    }
    private bool RetainingPlan => _result is { State.Plan: null, State.Eta.RouteUpdatePending: true }
        && _retainedResult?.DispatchId == _result.DispatchId && _retainedResult?.TruckId == _result.TruckId
        && !ArrivalCompleted && _arrivalMemory.PreviousDuringUpdate(_result.State.Eta, Clock.GetUtcNow().UtcDateTime) is not null;
    private AutomaticPlanningResult? DisplayResult => RetainingPlan ? _retainedResult : _result;
    private string? DisplayMessage => Client.Shared.RouteMessageDisplay.For(_result?.Message,
        DisplayResult?.State?.Plan is { InputsChanged: false, Tracking.AllStopsPassed: false });
    private bool ArrivalCompleted => _result?.State?.Plan?.Tracking.AllStopsPassed == true
        || Load?.Stops.Any(stop => stop.Id == (_result?.State?.Plan?.Tracking.NextStopId
            ?? _retainedResult?.State?.Plan?.Tracking.NextStopId)
            && (stop.DepartedAt ?? stop.DeliveredAt ?? stop.PickedUpAt).HasValue) == true;

    protected override async Task OnParametersSetAsync()
    {
        var url = TruckId.HasValue ? $"api/fleet/trucks/{TruckId}/planning" : $"api/dispatch/{Load?.Id}/planning/automatic";
        if (url == _url && _loadId == Load?.Id)
        {
            UpdateArrivalMemory();
            return;
        }
        _url = url;
        _loadId = Load?.Id;
        _retainedResult = null;
        _readVersion++;
        _request?.Cancel();
        _request?.Dispose();
        var request = new CancellationTokenSource();
        _request = request;
        var token = request.Token;
        ApplyResult(PlanningCache.Get(url));
        _error = null;
        await PublishForecastAsync(_result);
        await DisplayChanged.InvokeAsync();
        if (_disposed || token.IsCancellationRequested || url != _url) return;
        try { await RefreshAsync(url, token); }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { return; }
        if (!_disposed && !token.IsCancellationRequested && url == _url) _ = PollAsync(url, token);
    }

    private async Task RefreshAsync(string url, CancellationToken ct)
    {
        var version = _readVersion;
        _loading = true;
        await InvokeAsync(StateHasChanged);
        var response = await PlanningCache.RefreshAsync(url, ct);
        if (_disposed || ct.IsCancellationRequested || url != _url || version != _readVersion) return;
        if (response.Success)
        {
            ApplyResult(response.Response);
            _error = null;
            await PublishForecastAsync(_result);
        }
        else
        {
            if (response.HttpStatusCode is System.Net.HttpStatusCode.Unauthorized or System.Net.HttpStatusCode.Forbidden)
            { ApplyResult(null); _error = response.ErrorMessage; }
            else _error = "Route is temporarily unavailable. Retrying automatically.";
        }
        if (_disposed || ct.IsCancellationRequested || url != _url || version != _readVersion) return;
        _lastRefresh = Clock.GetUtcNow().UtcDateTime;
        _loading = false;
        await DisplayChanged.InvokeAsync();
        await InvokeAsync(StateHasChanged);
    }

    private void ApplyResult(AutomaticPlanningResult? result)
    {
        _result = result is not null && (TruckId is not { } truckId || result.TruckId == truckId)
            && (Load is not { } load || result.DispatchId == load.Id) ? result : null;
        if (_retainedResult?.DispatchId != _result?.DispatchId || _retainedResult?.TruckId != _result?.TruckId)
            _retainedResult = null;
        if (_result is { State.Plan: not null, State.Eta.RouteUpdatePending: false, State.Eta.Stops.Count: > 0 }
            && (_retainedResult is null || _result.State.Eta.CalculatedAt >= _retainedResult.State!.Eta!.CalculatedAt))
            _retainedResult = _result;
        UpdateArrivalMemory();
    }

    private void UpdateArrivalMemory()
    {
        _arrivalMemory.Update(_result?.DispatchId, ArrivalStop, _result?.State?.Eta,
            ArrivalCompleted);
    }

    private Task PublishForecastAsync(AutomaticPlanningResult? result)
    {
        if (!ForecastChanged.HasDelegate || result?.State?.Eta is not { } forecast
            || forecast.ValidUntil.ToUniversalTime() <= Clock.GetUtcNow().UtcDateTime
            || TruckId is { } truckId && result.TruckId != truckId
            || Load is { } load && result.DispatchId != load.Id) return Task.CompletedTask;
        return ForecastChanged.InvokeAsync(forecast);
    }

    private async Task PollAsync(string url, CancellationToken ct)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(10), Clock);
        using var pace = Visibility.Pace(timer, TimeSpan.FromSeconds(10));
        try { while (await timer.WaitForNextTickAsync(ct))
        {
            if (_result?.State?.Plan is null || Clock.GetUtcNow().UtcDateTime - _lastRefresh >= TimeSpan.FromMinutes(1))
                await RefreshAsync(url, ct);
        } }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
    }

    public void Dispose()
    {
        _disposed = true;
        _request?.Cancel();
        _request?.Dispose();
    }
}
