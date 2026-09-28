using System.Globalization;
using Client.Models.DTO;
using Client.Models.DTO.Dispatch;
using Client.Models.DTO.Fleet;
using Client.Models.DTO.Planning;
using Client.Services;
using Client.Shared.Dispatch;
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
      await PushStopCompletionsAsync();
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
      {
        _chainLoads =
          result
            .Response!.Items.FirstOrDefault(row => row.TruckId == truck)
            ?.Dispatches ?? [];
        await PushStopCompletionsAsync();
      }
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

  // The stop cards on the map hear which stops the server has completed,
  // from the same board read the chain shows.
  // A later trip's place among the next loads the map draws, which picks
  // its road's colour there.
  private int? NextRouteIndex(DispatchResponse load)
  {
    var routes = _nextLoadRoutes;
    for (var i = 0; i < routes.Count; i++)
      if (
        routes[i].Id == load.Id
        && routes[i].ExecutionLegId == load.ExecutionLegId
      )
        return i;
    return null;
  }

  // The chain's stop badges ("1 · P"), made once per chain read by their
  // one owner and shared by the trip cards and the map.
  private IReadOnlyList<DispatchResponse>? _badgesFor;
  private IReadOnlyDictionary<Guid, string> _stopBadges =
    new Dictionary<Guid, string>();
  private IReadOnlyDictionary<Guid, string> StopBadges
  {
    get
    {
      if (!ReferenceEquals(_badgesFor, _chainLoads))
      {
        _badgesFor = _chainLoads;
        _stopBadges = StopMarkers.ChainBadges(_chainLoads);
      }
      return _stopBadges;
    }
  }

  private async Task PushStopCompletionsAsync()
  {
    if (_map is null || _disposed)
      return;
    await _map.InvokeVoidAsync(
      "setStopBadges",
      StopBadges.ToDictionary(x => x.Key.ToString(), x => x.Value)
    );
    var completed = _chainLoads
      .SelectMany(load => load.Stops)
      .Where(stop => stop.IsCompleted)
      .Select(stop => new
      {
        id = stop.Id.ToString(),
        at = StopCompletion.Time(stop),
      })
      .ToArray();
    await _map.InvokeVoidAsync("setStopCompletions", (object)completed);
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
      var index =
        stop is { } id && route is not null
          ? route.Stops.ToList().FindIndex(x => x.Id == id)
          : -1;
      if (stop is { } chosen && (!ShowNextLoads || index < 0))
      {
        // No drawn road to open the stop on: the camera still goes to that
        // exact stop, never to another one in its place.
        var place = load.Stops.FirstOrDefault(x => x.Id == chosen);
        var places = load
          .Stops.Where(x => x.Latitude.HasValue && x.Longitude.HasValue)
          .Select(x => new[] { x.Latitude!.Value, x.Longitude!.Value })
          .ToArray();
        if (place is { Latitude: { } lat, Longitude: { } lng })
          await _map!.InvokeVoidAsync("centerStop", lat, lng, places);
      }
      else if (ShowNextLoads && route is not null)
      {
        if (stop is not null)
          await _map!.InvokeVoidAsync(
            "selectNextStop",
            load.Id.ToString(),
            index,
            load.ExecutionLegId?.ToString(),
            true
          );
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

  // The server's reading of the driver's logs, never the truck's motion:
  // parked or engine off is not rest (the owner, September 28). The
  // truck's own reading (GetTruckDutyStatus) comes first; a server without
  // it leaves the planning ETA's. Either is used only when fresh and in
  // agreement with the live status.
  private DriverDutyStatus? DutyReading
  {
    get
    {
      var duty =
        _truckDuty is { Duty: { } own } && _truckDuty.TruckId == _activeTruckId
          ? own
          : HeadDutyStatus;
      return
        _hos?.CurrentDutyStatus is { } live
        && duty is not null
        && duty.Status == live
        && duty.ObservedAt >= DateTimeOffset.UtcNow.AddMinutes(-3)
        ? duty
        : null;
    }
  }

  // How long the driver has been in that status, under its name.
  private string? DutySince =>
    _hos?.CurrentDutyStatus is null ? null
    : DutyReading?.StatusMinutes is { } minutes
      ? $"for {DriverDutySummary.Duration(minutes)}"
    : "Time in status unavailable";

  // While resting, the rest in its own labelled rows under the clocks: how
  // long so far, when the daily rest is complete and when the cycle reset
  // is - the server's times, never worked out here. A time the server did
  // not give reads Unavailable. A daily rest whose time has passed says it
  // is complete; whether the hours are back is the clocks' answer above.
  private IReadOnlyList<(string Label, string Value)> RestRows =>
    DutyReading is { RestMinutes: { } rest } duty
      ?
      [
        ("Rest so far", DriverDutySummary.Duration(rest)),
        (
          "10h rest complete",
          RestTime(duty.DailyRestCompleteAt, duty.DailyRestRemainingMinutes)
        ),
        (
          duty.CycleResetHours is > 0 and var hours
            ? $"{hours}h reset"
            : "Cycle reset",
          RestTime(duty.CycleResetCompleteAt, duty.CycleResetRemainingMinutes)
        ),
      ]
      : [];

  private string RestTime(DateTimeOffset? at, int? remaining) =>
    (at, remaining) switch
    {
      ({ } time, > 0) =>
        $"{RestClock(time)} (in {DriverDutySummary.Duration(remaining.Value)})",
      ({ } time, _) => $"Done at {RestClock(time)}",
      (null, > 0) => $"in {DriverDutySummary.Duration(remaining.Value)}",
      (null, 0) => "Done",
      _ => "Unavailable",
    };

  private static string RestClock(DateTimeOffset time) =>
    time.ToLocalTime().ToString("MMM d, h:mm tt", CultureInfo.InvariantCulture);

  // The panel shows where the truck is as its locality - town, region and
  // postal code - by the shared address formatter; the street stays in the
  // title and in what is copied. A location the formatter cannot split is
  // shown whole.
  private static string LocationSummary(string? location) =>
    string.IsNullOrWhiteSpace(location) ? "—"
    : StopAddressLines.Create(location).Locality is { Length: > 0 } locality
      ? locality
    : location;
}
