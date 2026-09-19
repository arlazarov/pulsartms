using Client.Models.DTO.Dispatch;
using Client.Models.DTO.Planning;
using Client.Services;

namespace Client.Pages.FleetMap;

public partial class FleetMap
{
  private DispatchResponse? _loadDetails;
  private int _loadDetailsVersion;
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

  private FuelStopArrival? ScheduledFuelArrival
  {
    get
    {
      if (
        _routeState?.Plan is not { DispatchId: var dispatchId }
        || ScheduledStop is not { Id: var stopId }
      )
        return null;
      var arrival = _routeState.FuelStopArrivals.FirstOrDefault(value =>
        value.DispatchId == dispatchId && value.StopId == stopId
      );
      return
        arrival is not null
        && double.IsFinite(arrival.Gallons)
        && double.IsFinite(arrival.Percent)
        && arrival.Gallons >= 0
        && arrival.Percent is >= 0 and <= 100
        ? arrival
        : null;
    }
  }

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
