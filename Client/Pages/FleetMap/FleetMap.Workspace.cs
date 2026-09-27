using System.Globalization;
using Client.Models.DTO;
using Client.Models.DTO.Dispatch;
using Client.Models.DTO.Fleet;
using Client.Models.DTO.Planning;
using Client.Shared.DriverStatus.DriverDutySummary;
using Client.Shared.Trucks;
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;

namespace Client.Pages.FleetMap;

// The workspace around the map: the truck list and its motion chips, the
// map's own buttons, the trip chain and the one chosen trip under the
// truck. Each routes into the page's existing selection and camera paths.
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

  private static readonly (string Motion, string Label)[] MotionChoices =
  [
    ("all", "All"),
    ("moving", "Moving"),
    ("stopped", "Stopped"),
  ];

  private string _motion = "all";
  private bool _layersOpen;

  private static bool InMotion(TruckLocationMapDto truck, string motion) =>
    motion switch
    {
      "moving" => TruckMotion.IsMoving(truck.Speed),
      "stopped" => !TruckMotion.IsMoving(truck.Speed),
      _ => true,
    };

  private int MotionCount(string motion) =>
    MatchingTrucks.Count(truck => InMotion(truck, motion));

  private List<TruckLocationMapDto> ListedTrucks =>
    _motion == "all"
      ? MatchingTrucks
      : MatchingTrucks.Where(truck => InMotion(truck, _motion)).ToList();

  private async Task ShowFleetAsync()
  {
    if (_map is null || _disposed)
      return;
    try
    {
      await _map.InvokeVoidAsync("showFleet");
    }
    catch (JSException) { }
  }

  private async Task ZoomAsync(int step)
  {
    if (_map is null || _disposed)
      return;
    try
    {
      await _map.InvokeVoidAsync("zoomBy", step);
    }
    catch (JSException) { }
  }

  private Task ShowRouteTabAsync() =>
    _inspectorMode == MapInspectorMode.Truck
      ? Task.CompletedTask
      : BackToTruckAsync();

  private Task ShowFuelTabAsync() =>
    _inspectorMode == MapInspectorMode.Truck
      ? OpenFuelPlanAsync()
      : Task.CompletedTask;

  // The panel's motion fact: what the map's shape says, with the reported
  // speed; nothing when the speed is not known (stale GPS).
  private string MotionFact(TruckLocationMapDto truck) =>
    KnownSpeed(truck) is not { } speed ? "—"
    : TruckMotion.IsMoving(speed)
      ? $"Moving · {speed.ToString("0", CultureInfo.InvariantCulture)} mph"
    : "Stopped";

  private string DutyFact =>
    _hos?.CurrentDutyStatus is { } status
      ? DriverDutySummary.StatusName(status)
      : "—";

  // The list's Load and Next stop: the fleet's planning summaries, read in
  // one request by the shared planning cache (its own two-minute guard),
  // the same entries this page and Dispatch read for one truck. The list
  // shows them at the next position poll's render; none is forced here.
  private async Task PreloadListWorkAsync()
  {
    try
    {
      await PlanningCache.PreloadAsync();
    }
    catch (Exception ex) when (IsLoadError(ex)) { }
  }

  private FleetTruckWork TruckWork(Guid truckId)
  {
    var result = PlanningCache.Get($"api/fleet/trucks/{truckId}/planning");
    var plan = result?.State?.Plan;
    var next = plan?.Stops.FirstOrDefault(stop =>
      stop.Id == plan.Tracking.NextStopId
    );
    return new(result?.LoadNumber, next?.Name);
  }
}
