using Client.Models.DTO.Dispatch;
using Client.Models.DTO.Planning;

namespace Client.Pages.FleetMap;

public partial class FleetMap
{
  private DispatchResponse? _loadDetails;
  private int _loadDetailsVersion;
  private readonly Client.Services.ArrivalDisplayMemory _arrivalMemory = new();
  private PlanStop? ScheduledStop
  {
    get
    {
      if (_routeState?.Plan is { } plan)
        return plan.Stops.FirstOrDefault(s => s.Id == plan.Tracking.NextStopId)
          ?? plan.ReferenceStops?.FirstOrDefault(s => s.Id == plan.Tracking.NextStopId);
      if (_arrivalMemory.DispatchId == SelectedDispatchId && _arrivalMemory.Stop is { } retainedStop) return retainedStop;
      var stop = _loadDetails?.Stops.OrderBy(s => s.Sequence).FirstOrDefault(s =>
        s.Job.Contains("pick", StringComparison.OrdinalIgnoreCase) ? s.PickedUpAt is null :
        s.Job.Contains("drop", StringComparison.OrdinalIgnoreCase) ? s.DeliveredAt is null : s.DepartedAt is null);
      return stop is null ? null : new PlanStop(Guid.Empty, stop.Name, stop.Address, stop.Sequence, new(0, 0))
      {
        Job = stop.Job, ScheduledDate = stop.ScheduledDate, ScheduledTime = stop.ScheduledTime,
        ScheduledDate2 = stop.ScheduledDate2, ScheduledTime2 = stop.ScheduledTime2
      };
    }
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
        var response = await Api.GetAsync<DispatchResponse>($"api/dispatch/{id}", _lifetime.Token);
        if (response.Success) load = response.Response;
      }
      else if (_activeTruckId is { } truckId)
      {
        var response = await Api.GetAsync<List<DispatchResponse>>($"api/dispatch/truck/{truckId}", _lifetime.Token);
        if (response.Success) load = response.Response?.FirstOrDefault();
      }
      if (_disposed || version != _loadDetailsVersion) return;
      _loadDetails = load;
      await SendMapLoadReferenceAsync();
      await InvokeAsync(StateHasChanged);
    }
    catch (OperationCanceledException) when (_lifetime.IsCancellationRequested) { }
  }
}
