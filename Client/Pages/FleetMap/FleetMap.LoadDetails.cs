using Client.Models.DTO.Dispatch;
using Client.Models.DTO.Planning;
using Client.Services;

namespace Client.Pages.FleetMap;

public partial class FleetMap
{
  private DispatchResponse? _loadDetails;
  private int _loadDetailsVersion;

  // The truck panel's arrival, kept while the panel is mounted (hidden in
  // the other views) so coming back to the truck shows the same forecast.
  private readonly ArrivalDisplayMemory _arrivalMemory = new();

  private bool NextStopIsFinal =>
    _routeState?.Plan
      is { InputsChanged: false, Tracking.AllStopsPassed: false } plan
    && plan.Tracking.NextStopId is { } next
    && plan.Stops.LastOrDefault()?.Id == next;
  private DispatchStopResponse? NextLoadDetailsStop =>
    _loadDetails
      ?.Stops.OrderBy(s => s.Sequence)
      .FirstOrDefault(s => !s.DriverOnly && !s.IsCompleted);

  // The booking beside the panel's ETA: the tracked stop's own. The
  // estimate beside it keeps only a forecast for that same stop, so the
  // two times shown together are always about one visit. A stop with no
  // booking shows no row (the owner, September 27).
  private PlanStop? HeadAppointmentStop =>
    ScheduledStop is { } stop
    && (stop.ScheduledDate is not null || stop.ScheduledTime is not null)
      ? stop
      : null;

  private string HeadAppointmentText(PlanStop? stop)
  {
    if (stop is null)
      return "—";
    var booked = FleetAppointmentDisplay.Split(stop);
    var text = booked.Date is null
      ? booked.Value
      : $"{booked.Date} · {booked.Value}";
    // An ordinary space before the zone: at 320 px and 200 % text the
    // clock and zone held together were wider than the card.
    return StopZone(stop) is { } zone ? $"{text} {zone}" : text;
  }

  // A window that ends on another day is two lines, the start over the
  // end: on one line it broke wherever the card ran out, mid-date (the
  // owner, September 26).
  private IReadOnlyList<string> HeadAppointmentLines(PlanStop? stop)
  {
    var text = HeadAppointmentText(stop);
    if (
      stop?.ScheduledDate2 is { } end
      && end != stop.ScheduledDate
      && text.IndexOf(" – ", StringComparison.Ordinal) is > 0 and var at
    )
      return [text[..at], text[(at + 3)..]];
    return [text];
  }

  // The forecast for the same stop names the zone the booking is read in.
  private string? StopZone(PlanStop stop) =>
    StopTimeZoneLabel.ForAppointment(
      stop,
      (
        _routeState?.Plan is { InputsChanged: false }
          ? DisplayRouteState?.Eta
          : null
      )
        ?.Stops.FirstOrDefault(value => value.StopId == stop.Id)
        ?.TimeZoneId
    );

  private PlanStop? ScheduledStop
  {
    get
    {
      if (_routeState?.Plan is { InputsChanged: false } plan)
        return WithStopAppointment(
          plan.Stops.FirstOrDefault(s => s.Id == plan.Tracking.NextStopId)
            ?? plan.ReferenceStops?.FirstOrDefault(s =>
              s.Id == plan.Tracking.NextStopId
            )
        );
      if (
        !RouteInputsChanged
        && _arrivalMemory.DispatchId == SelectedDispatchId
        && _arrivalMemory.Stop is { } retainedStop
      )
        return WithStopAppointment(retainedStop);
      var stop = NextLoadDetailsStop;
      return stop is null
        ? null
        : new PlanStop(
          stop.Id,
          stop.Name,
          stop.Address,
          stop.Sequence,
          new(0, 0)
        )
        {
          Job = stop.Job,
          StateAfter = stop.StateAfter,
          ScheduledDate = stop.ScheduledDate,
          ScheduledTime = stop.ScheduledTime,
          ScheduledDate2 = stop.ScheduledDate2,
          ScheduledTime2 = stop.ScheduledTime2,
          AppointmentTimeZoneId = stop.AppointmentTimeZoneId,
        };
    }
  }

  private PlanStop? WithStopAppointment(PlanStop? stop)
  {
    if (
      stop is null
      || stop.ScheduledDate.HasValue
      || _loadDetails is not { } load
      || load.Id != SelectedDispatchId
      || load.Stops.FirstOrDefault(value => value.Id == stop.Id)
        is not { ScheduledDate: not null } details
    )
      return stop;
    return stop with
    {
      ScheduledDate = details.ScheduledDate,
      ScheduledTime = details.ScheduledTime,
      ScheduledDate2 = details.ScheduledDate2,
      ScheduledTime2 = details.ScheduledTime2,
      AppointmentTimeZoneId = details.AppointmentTimeZoneId,
    };
  }

  private async Task LoadDispatchDetailsAsync()
  {
    var version = ++_loadDetailsVersion;
    _loadDetails = null;
    var id = SelectedDispatchId;
    try
    {
      DispatchResponse? load = null;
      if (id.HasValue)
      {
        var response = await Api.GetAsync<DispatchResponse>(
          $"api/dispatch/{id}",
          _lifetime.Token
        );
        if (response.Success)
          load = response.Response;
      }
      else if (_activeTruckId is { } truckId)
      {
        var response = await Api.GetAsync<List<DispatchResponse>>(
          $"api/dispatch/truck/{truckId}",
          _lifetime.Token
        );
        if (response.Success)
          load = response.Response?.FirstOrDefault();
      }
      if (_disposed || version != _loadDetailsVersion)
        return;
      _loadDetails = load;
      await SendMapLoadReferenceAsync();
      await InvokeAsync(StateHasChanged);
    }
    catch (OperationCanceledException) when (_lifetime.IsCancellationRequested)
    { }
  }
}
