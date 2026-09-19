using Client.Models.DTO.Dispatch;
using Client.Models.DTO.Planning;
using Client.Services;
using Microsoft.AspNetCore.Components;

namespace Client.Shared.Dispatch.DispatchCycleForecast;

public partial class DispatchCycleForecast
{
  [Inject]
  private TimeProvider Clock { get; set; } = default!;

  [Parameter, EditorRequired]
  public DispatchResponse Load { get; set; } = default!;

  [Parameter]
  public bool Refreshing { get; set; }

  [Parameter]
  public bool Detailed { get; set; } = true;
  private ArrivalDisplayMemory Memory { get; } = new();
  private StopCycleForecast? Forecast { get; set; }
  private bool HasStopHours { get; set; }
  private bool HasLegacyCycle =>
    !HasStopHours && Forecast?.RemainingMinutes is >= 0;
  private string CycleRemaining => DispatchCycleDisplay.Remaining(Forecast);

  protected override void OnParametersSet()
  {
    var finalStop = Load.Stops.OrderBy(stop => stop.Sequence).LastOrDefault();
    var completed =
      finalStop is null
      || finalStop.Id == Guid.Empty
      || Load.Id == Guid.Empty
      || finalStop.IsCompleted;
    var plannedStop = finalStop is null
      ? null
      : new PlanStop(
        finalStop.Id,
        finalStop.Name,
        string.Join(
          ", ",
          new[]
          {
            finalStop.Address,
            finalStop.City,
            finalStop.Province,
            finalStop.ZipCode,
            finalStop.Country,
          }.Where(part => !string.IsNullOrWhiteSpace(part))
        ),
        finalStop.Sequence,
        new(
          (double)(finalStop.Latitude ?? 0),
          (double)(finalStop.Longitude ?? 0)
        )
      )
      {
        Job = finalStop.Job,
        ScheduledDate = finalStop.ScheduledDate ?? Load.DeliveryDate,
        ScheduledTime = finalStop.ScheduledTime,
        ScheduledDate2 = finalStop.ScheduledDate2,
        ScheduledTime2 = finalStop.ScheduledTime2,
      };
    var match = completed
      ? null
      : Load.Eta?.Stops.FirstOrDefault(estimate =>
        estimate.DispatchId == Load.Id && estimate.StopId == finalStop!.Id
      );
    var estimate = Load.Eta is null
      ? null
      : Load.Eta with
      {
        Stops = match is null ? [] : [match],
      };
    Memory.Update(Load.Id, plannedStop, estimate, completed);
    var now = Clock.GetUtcNow().UtcDateTime;
    var display = completed ? null : Memory.Display(estimate, now, Refreshing);
    Forecast = display?.Stops.FirstOrDefault()?.CycleAfterDeparture;
    HasStopHours = display?.Stops.FirstOrDefault()?.Hours is not null;
  }
}
