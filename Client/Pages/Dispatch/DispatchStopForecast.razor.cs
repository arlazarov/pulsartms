using Client.Models.DTO.Dispatch;
using Client.Models.DTO.Planning;
using Client.Shared.Dispatch;
using Microsoft.AspNetCore.Components;

namespace Client.Pages.Dispatch;

public partial class DispatchStopForecast
{
  [Inject]
  private TimeProvider Clock { get; set; } = default!;

  [Parameter, EditorRequired]
  public DispatchResponse Load { get; set; } = default!;

  [Parameter]
  public Guid StopId { get; set; }

  [Parameter]
  public bool Detailed { get; set; }

  [Parameter]
  public bool DraftChanged { get; set; }

  private PlanStop? _stop;
  private DispatchEta? _eta;
  private bool _completed;
  private bool _driverOnly;
  private StopCycleForecast? LegacyCycle =>
    _eta?.Stops.FirstOrDefault() is { Hours: null } match
      ? match.CycleAfterDeparture
      : null;

  protected override void OnParametersSet()
  {
    var source = Load.Stops.FirstOrDefault(stop => stop.Id == StopId);
    _completed =
      source is not null
      && (
        source.IsCompleted
        || !source.DriverOnly && DispatchBoardRow.IsCompleted(Load)
      );
    _driverOnly = source?.DriverOnly == true;
    _stop = source is null
      ? null
      : new PlanStop(
        source.Id,
        source.Name,
        string.Join(
          ", ",
          new[]
          {
            source.Address,
            source.City,
            source.Province,
            source.ZipCode,
            source.Country,
          }.Where(x => !string.IsNullOrWhiteSpace(x))
        ),
        source.Sequence,
        new((double)(source.Latitude ?? 0), (double)(source.Longitude ?? 0))
      )
      {
        Job = source.Job,
        ScheduledDate = source.ScheduledDate,
        ScheduledTime = source.ScheduledTime,
        ScheduledDate2 = source.ScheduledDate2,
        ScheduledTime2 = source.ScheduledTime2,
      };
    var match =
      !DraftChanged
      && !_completed
      && !_driverOnly
      && Load.Id != Guid.Empty
      && StopId != Guid.Empty
      && Load.Eta is { } eta
      && eta.ValidUntil.ToUniversalTime() > Clock.GetUtcNow().UtcDateTime
        ? eta.Stops.FirstOrDefault(x =>
          x.DispatchId == Load.Id && x.StopId == StopId
        )
        : null;
    _eta =
      match is null || source is null
        ? null
        : Load.Eta! with
        {
          Stops = [match],
        };
  }
}
