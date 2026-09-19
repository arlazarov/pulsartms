using Client.Models.DTO;
using Client.Models.DTO.Dispatch;
using Client.Shared.Dispatch;
using Microsoft.AspNetCore.Components;

namespace Client.Pages.Dispatch;

public partial class DispatchPapers
{
  [CascadingParameter]
  public DispatchSettingsState? DisplaySettings { get; set; }

  [Parameter, EditorRequired]
  public IReadOnlyList<TruckDispatchBoardResponse> Trucks { get; set; } = [];

  [Parameter]
  public bool Completed { get; set; }

  [Parameter]
  public bool Refreshing { get; set; }

  [Parameter]
  public Func<
    TruckDispatchBoardResponse,
    DispatchResponse,
    string
  >? LoadPhase { get; set; }

  private string Phase(DispatchBoardRow row) =>
    Completed || row.Completed
      ? "Completed"
      : LoadPhase?.Invoke(row.Truck, row.Load)
        ?? (row.Planned ? "Planned" : "");

  private string LoadLabel(int number) =>
    LoadNumberDisplay.Format(number, DisplaySettings?.LoadNumberPrefix);

  private string[] Titles =>
    Completed
      ? ["Completed loads"]
      :
      [
        "Awaiting pickup",
        "In transit",
        "Delivery / pickup today and tomorrow",
      ];
  private List<DispatchBoardRow>[] _columns =
  [
    [],
    [],
    [],
  ];
  private DateOnly _today;

  private bool ShowDelivery(DispatchBoardRow row) =>
    row.Completed ? true
    : row.Planned ? false
    : InDateWindow(row.PickupDate) && !InDateWindow(row.DeliveryDate) ? false
    : InDateWindow(row.DeliveryDate) && !InDateWindow(row.PickupDate) ? true
    : row.InTransit;

  private bool InDateWindow(DateOnly? date) =>
    DispatchBoardRow.IsTodayOrTomorrow(date, _today);

  private string EventLabel(DispatchBoardRow row) =>
    ShowDelivery(row) ? "Delivery"
    : row.Origin?.Job is "Pick Up" or "Pickup" ? "Pickup"
    : DispatchBoardRow.Text(row.Origin?.Job, "Stop");

  private string EventSchedule(DispatchBoardRow row) =>
    DispatchBoardRow.Schedule(
      ShowDelivery(row) ? row.Destination : row.Origin,
      ShowDelivery(row) ? row.Load.DeliveryDate : row.Load.ShipDate
    );

  protected override void OnParametersSet()
  {
    _today = DateOnly.FromDateTime(DateTime.Today);
    var rows = Trucks
      .SelectMany(truck =>
        truck.Dispatches.Select(load => new DispatchBoardRow(truck, load))
      )
      .ToList();
    for (var i = 0; i < 3; i++)
    {
      _columns[i] = Completed
        ? i == 0
          ? rows
          : []
        : rows.Where(x => x.Column(_today) == i)
          .OrderBy(x =>
            (ShowDelivery(x) ? x.DeliveryDate : x.PickupDate)
            ?? DateOnly.MaxValue
          )
          .ThenBy(x =>
            (
              ShowDelivery(x)
                ? x.Destination?.ScheduledTime
                : x.Origin?.ScheduledTime
            ) ?? TimeOnly.MaxValue
          )
          .ThenBy(x => x.Load.LoadNumber)
          .ToList();
    }
  }
}
