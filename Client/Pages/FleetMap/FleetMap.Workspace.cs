using System.Globalization;
using Client.Models.DTO;
using Client.Models.DTO.Dispatch;
using Client.Models.DTO.Planning;
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;

namespace Client.Pages.FleetMap;

// The workspace around the map: a truck list, the trip chain and the one
// chosen trip under the truck. Both only route into the page's existing selection paths, so
// selection, Follow and the inspector behave as in the current interface.
public partial class FleetMap
{
  // What the map is showing, as the map's own zoom policy chose it
  // (hybrid at close zoom, the road map further out).
  private string? _mapType;

  [JSInvokable]
  public Task OnMapTypeChanged(string mapType)
  {
    if (_disposed || mapType == _mapType)
      return Task.CompletedTask;
    _mapType = mapType;
    return InvokeAsync(StateHasChanged);
  }

  // Hiding the fleet list gives its width to the map; kept for the visit.
  private bool _listCollapsed;

  // The chain is the selected truck's row of the Dispatch board: the same
  // server read, order and work phase (DispatchResponse.WorkPhase) the
  // board shows, asked for one truck. Read when the truck changes and at
  // the board's own one-minute cadence; a late answer for another truck is
  // dropped.
  private static readonly TimeSpan ChainRefreshInterval = TimeSpan.FromMinutes(
    1
  );
  private IReadOnlyList<DispatchResponse> _chainLoads = [];
  private Guid? _chainTruckId;
  private DateTimeOffset _chainReadAt;
  private bool _chainLoading;
  private bool _chainFailed;
  private CancellationTokenSource? _chainRequest;

  private async Task RefreshChainIfDueAsync()
  {
    if (_disposed)
      return;
    var truck = _activeTruckId;
    if (truck is null)
    {
      if (_chainTruckId is null)
        return;
      _chainRequest?.Cancel();
      _chainTruckId = null;
      _chainLoads = [];
      _chainLoading = _chainFailed = false;
      StateHasChanged();
      return;
    }
    if (
      truck == _chainTruckId
      && (
        _chainRequest is not null
        || Clock.GetUtcNow() - _chainReadAt < ChainRefreshInterval
      )
    )
      return;
    var changed = truck != _chainTruckId;
    _chainRequest?.Cancel();
    using var request = CancellationTokenSource.CreateLinkedTokenSource(
      _lifetime.Token
    );
    _chainRequest = request;
    _chainTruckId = truck;
    _chainReadAt = Clock.GetUtcNow();
    if (changed)
    {
      _chainLoads = [];
      _chainLoading = true;
      _chainFailed = false;
      _trip = null;
      _tripStop = null;
      await FocusMapStopAsync(null);
      StateHasChanged();
    }
    var date = DateOnly
      .FromDateTime(Clock.GetLocalNow().DateTime)
      .ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
    try
    {
      var result = await Api.GetAsync<
        PaginatedListDTO<TruckDispatchBoardResponse>
      >(
        $"api/dispatch/board?page=1&pageSize=12&search=&date={date}"
          + $"&truckId={truck}&includePlanned=true"
          + "&includeHos=false&includeFinancials=false&includeEta=true",
        request.Token
      );
      if (
        _disposed
        || request.IsCancellationRequested
        || _chainTruckId != truck
      )
        return;
      _chainLoading = false;
      _chainFailed = !result.Success || result.Response is null;
      if (!_chainFailed)
        _chainLoads =
          result
            .Response!.Items.FirstOrDefault(row => row.TruckId == truck)
            ?.Dispatches ?? [];
      StateHasChanged();
    }
    catch (OperationCanceledException) when (request.IsCancellationRequested)
    { }
    finally
    {
      if (ReferenceEquals(_chainRequest, request))
        _chainRequest = null;
    }
  }

  private void DisposeChain()
  {
    _chainRequest?.Cancel();
    _chainRequest = null;
  }

  // As choosing a truck in search: focus it on the map, then select it.
  private async Task ChooseListTruckAsync(Guid truckId)
  {
    if (_map is null || _disposed)
      return;
    if (truckId == _activeTruckId)
    {
      await SelectRouteAsync(truckId, _activeDispatchId);
      return;
    }
    if (
      await _map.InvokeAsync<bool>("focusTruck", truckId.ToString(), (int?)null)
    )
      await SelectRouteAsync(truckId, null);
  }

  // Back to the truck and its current load; the camera does not move, so an
  // active Follow continues.
  private Task ChooseCurrentLoadAsync() =>
    _activeTruckId is { } truck
      ? SelectRouteAsync(truck, _activeDispatchId)
      : Task.CompletedTask;

  // One trip is shown in full under the truck: the one chosen in the
  // chain or on the map, else the current one. A stop may be chosen in it.
  private (Guid Id, Guid? Leg)? _trip;
  private Guid? _tripStop;
  private bool _stopChoiceSent;

  private DispatchResponse? SelectedTrip =>
    (
      _trip is { } key
        ? _chainLoads.FirstOrDefault(load =>
          load.Id == key.Id && load.ExecutionLegId == key.Leg
        )
        : null
    )
    ?? _chainLoads.FirstOrDefault(load => load.Id == SelectedDispatchId)
    ?? _chainLoads.FirstOrDefault();

  private bool IsCurrentTrip(DispatchResponse load) =>
    load.Id == SelectedDispatchId;

  // The map learns once that its current-route badges choose a stop here.
  private async Task SendStopChoiceAsync()
  {
    if (_map is null || _disposed || _stopChoiceSent)
      return;
    _stopChoiceSent = true;
    await _map.InvokeVoidAsync("setStopChoice", true);
  }

  private Task ChooseChainLoadAsync(DispatchResponse load) =>
    ChooseTripAsync(load, null);

  private Task ChooseChainStopAsync(
    (DispatchResponse Load, Guid Stop) choice
  ) => ChooseTripAsync(choice.Load, choice.Stop);

  private Task ChooseTripStopAsync(Guid? stop) =>
    SelectedTrip is { } trip ? ChooseTripAsync(trip, stop) : Task.CompletedTask;

  // Chooses a trip, and a stop in it, then shows the same on the map: the
  // current trip's stop is highlighted where it stands; a later trip is
  // picked through the next-loads layer, as a click on it would. The camera
  // is not moved, so Follow continues.
  private async Task ChooseTripAsync(DispatchResponse load, Guid? stop)
  {
    if (_map is null || _disposed)
      return;
    _trip = (load.Id, load.ExecutionLegId);
    _tripStop = stop;
    if (stop is not null)
      _mobileTruckDetailsOpen = true;
    if (IsCurrentTrip(load))
    {
      await _map.InvokeVoidAsync("clearNextLoadSelection");
      await FocusMapStopAsync(stop);
    }
    else
    {
      await FocusMapStopAsync(null);
      var route = _nextLoadRoutes.FirstOrDefault(route =>
        route.Id == load.Id && route.ExecutionLegId == load.ExecutionLegId
      );
      if (ShowNextLoads && route is not null)
      {
        var index = stop is { } id
          ? Math.Max(0, route.Stops.ToList().FindIndex(x => x.Id == id))
          : 0;
        await _map.InvokeVoidAsync(
          "selectNextStop",
          load.Id.ToString(),
          index,
          load.ExecutionLegId?.ToString()
        );
      }
    }
    StateHasChanged();
  }

  private async Task FocusMapStopAsync(Guid? stop)
  {
    if (_map is not null && !_disposed)
      await _map.InvokeVoidAsync("focusRouteStop", stop?.ToString());
  }

  // A current-route badge pressed on the map.
  [JSInvokable]
  public Task OnRouteStopChosen(string stopId)
  {
    if (
      _disposed
      || !Guid.TryParse(stopId, out var stop)
      || _chainLoads.FirstOrDefault(IsCurrentTrip) is not { } current
    )
      return Task.CompletedTask;
    return InvokeAsync(() => ChooseTripAsync(current, stop));
  }

  // A later trip's badge pressed on the map, or picked from the chain: it
  // chooses that trip and stop in the panel under the truck.
  private Task OnTripStopChosenAsync(
    string truck,
    string? load,
    int stopIndex,
    string? executionLeg
  )
  {
    if (
      _disposed
      || !Guid.TryParse(truck, out var truckId)
      || truckId != _activeTruckId
    )
      return Task.CompletedTask;
    if (load is null || !Guid.TryParse(load, out var id))
    {
      // The layer lets go of a later trip - also when the current trip is
      // chosen, which clears it. Only a later trip's choice ends here.
      if (SelectedTrip is { } shown && IsCurrentTrip(shown))
        return Task.CompletedTask;
      _trip = null;
      _tripStop = null;
      return InvokeAsync(StateHasChanged);
    }
    Guid? leg = Guid.TryParse(executionLeg, out var parsed) ? parsed : null;
    var chosen = _chainLoads.FirstOrDefault(x =>
      x.Id == id && x.ExecutionLegId == leg
    );
    if (chosen is null)
      return Task.CompletedTask;
    var route = _nextLoadRoutes.FirstOrDefault(x =>
      x.Id == id && x.ExecutionLegId == leg
    );
    _trip = (id, leg);
    _tripStop =
      route is not null && stopIndex >= 0 && stopIndex < route.Stops.Count
        ? route.Stops[stopIndex].Id
        : null;
    if (_tripStop is not null)
      _mobileTruckDetailsOpen = true;
    return InvokeAsync(StateHasChanged);
  }

  private async Task ShowNextLoadsFromChainAsync()
  {
    if (ShowNextLoads)
      return;
    ShowNextLoads = true;
    await OnNextLoadsChanged();
  }
}
