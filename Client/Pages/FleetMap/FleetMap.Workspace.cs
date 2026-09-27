using System.Globalization;
using Client.Models.DTO;
using Client.Models.DTO.Dispatch;
using Client.Models.DTO.Fleet;
using Client.Models.DTO.Planning;
using Client.Services;
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

  private Task ChooseChainLoadAsync(DispatchResponse load) =>
    ChooseTripAsync(load, null);

  private Task ChooseChainStopAsync(
    (DispatchResponse Load, Guid Stop) choice
  ) => ChooseTripAsync(choice.Load, choice.Stop);

  // A trip chosen in the chain brings all of its road into view; one of its
  // stops opens that stop's card, as its badge on the map does, and brings
  // the camera to it. The current trip goes through the route's own owner,
  // a later one through the next-loads layer.
  // Each choice is numbered: one that another choice, another truck or the
  // page's end overtook while it waited on the map stops where it is.
  private int _tripChoiceVersion;

  private async Task ChooseTripAsync(DispatchResponse load, Guid? stop)
  {
    if (_map is null || _disposed)
      return;
    var version = ++_tripChoiceVersion;
    var truck = _activeTruckId;
    bool Current() =>
      !_disposed
      && _map is not null
      && version == _tripChoiceVersion
      && truck == _activeTruckId;
    _trip = (load.Id, load.ExecutionLegId);
    _tripStop = stop;
    if (stop is not null)
      _mobileTruckDetailsOpen = true;
    if (IsCurrentTrip(load))
    {
      await _map.InvokeVoidAsync("clearNextLoadSelection");
      if (!Current())
        return;
      await FocusMapStopAsync(stop);
      if (!Current())
        return;
      if (stop is { } id)
        await _map!.InvokeVoidAsync("openRouteStop", id.ToString());
      else if (CanShowRoute)
        await ShowRouteAsync();
    }
    else
    {
      await FocusMapStopAsync(null);
      if (!Current())
        return;
      var route = _nextLoadRoutes.FirstOrDefault(route =>
        route.Id == load.Id && route.ExecutionLegId == load.ExecutionLegId
      );
      if (ShowNextLoads && route is not null)
      {
        if (stop is { } id)
        {
          // The exact stop or none: a stop the drawn road does not have
          // never opens another one in its place.
          var index = route.Stops.ToList().FindIndex(x => x.Id == id);
          if (index >= 0)
            await _map!.InvokeVoidAsync(
              "selectNextStop",
              load.Id.ToString(),
              index,
              load.ExecutionLegId?.ToString()
            );
        }
        else
          await _map!.InvokeVoidAsync(
            "fitNextLoad",
            load.Id.ToString(),
            load.ExecutionLegId?.ToString()
          );
      }
    }
    if (Current())
      StateHasChanged();
  }

  private async Task FocusMapStopAsync(Guid? stop)
  {
    if (_map is not null && !_disposed)
      await _map.InvokeVoidAsync("focusRouteStop", stop?.ToString());
  }

  // A later trip's badge pressed on the map, or picked from the chain: it
  // chooses that trip and stop, and the chain marks them.
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

  private async Task ChooseMotionAsync(string motion)
  {
    if (_motion == motion)
      return;
    _motion = motion;
    await UpdateTruckSearchAsync();
  }

  private int MotionCount(string motion) =>
    MatchingTrucks.Count(truck => InMotion(truck, motion));

  private List<TruckLocationMapDto> ListedTrucks =>
    _motion == "all"
      ? MatchingTrucks
      : MatchingTrucks.Where(truck => InMotion(truck, _motion)).ToList();

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

  // The panel shows where the truck is as its locality - town, region and
  // postal code - by the shared address formatter; the street stays in the
  // title and in what is copied. A location the formatter cannot split is
  // shown whole.
  private static string LocationSummary(string? location) =>
    string.IsNullOrWhiteSpace(location) ? "—"
    : StopAddressLines.Create(location).Locality is { Length: > 0 } locality
      ? locality
    : location;

  // The panel's location, copied whole through the page's clipboard call.
  // "Copied" is said only once the browser took it; a refusal says so.
  private string? _locationCopy;
  private Guid? _locationCopyTruck;
  private int _locationCopyVersion;

  private async Task CopyLocationAsync(string location)
  {
    var version = ++_locationCopyVersion;
    var truck = _activeTruckId;
    string status;
    try
    {
      await JS.InvokeVoidAsync("navigator.clipboard.writeText", location);
      status = "Copied";
    }
    catch (JSException)
    {
      status = "Could not copy";
    }
    // A copy that finishes after another truck was chosen says nothing on
    // that truck's card.
    if (_disposed || version != _locationCopyVersion || truck != _activeTruckId)
      return;
    _locationCopy = status;
    _locationCopyTruck = truck;
    StateHasChanged();
    try
    {
      await Task.Delay(TimeSpan.FromSeconds(2), _lifetime.Token);
    }
    catch (OperationCanceledException)
    {
      return;
    }
    if (!_disposed && version == _locationCopyVersion)
    {
      _locationCopy = null;
      StateHasChanged();
    }
  }
}
