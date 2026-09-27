using Client.Models.DTO;
using Client.Models.DTO.Dispatch;
using Client.Services;
using Client.Shared.Dispatch;
using Microsoft.AspNetCore.Components;

namespace Client.Pages.Dispatch;

public partial class DispatchPapers
{
  [CascadingParameter]
  public DispatchSettingsState? DisplaySettings { get; set; }

  // The list's own address, so the load page can return to it.
  [Parameter]
  public string? ReturnOrigin { get; set; }

  private string LoadUrl(DispatchBoardRow row) =>
    ReturnNavigation.Load(row.Load.Id, ReturnOrigin);

  [Parameter, EditorRequired]
  public IReadOnlyList<TruckDispatchBoardResponse> Trucks { get; set; } = [];

  [Parameter]
  public bool Refreshing { get; set; }

  [Parameter]
  public Func<
    TruckDispatchBoardResponse,
    DispatchResponse,
    string
  >? LoadPhase { get; set; }

  private string Phase(DispatchBoardRow row) =>
    row.Completed
      ? "Completed"
      : LoadPhase?.Invoke(row.Truck, row.Load)
        ?? (row.Planned ? "Planned" : "");

  private string LoadLabel(int number) =>
    LoadNumberDisplay.Format(number, DisplaySettings?.LoadNumberPrefix);

  private static readonly string[] Titles =
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

  // The event a folder is about: its pickup until that is done, then its
  // delivery. A pickup already done never orders or names the folder (the
  // owner, September 27).
  private bool ShowDelivery(DispatchBoardRow row) =>
    row.Completed || row.OriginCompleted ? true
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
    // The work in the order it comes: by the nearest event not yet done;
    // and a load a truck has started - its pickup done - stands before that
    // truck's other loads, so a current delivery comes before the pickup
    // that follows it on the same truck (the owner, September 27).
    var due = rows.ToDictionary(row => row, Due);
    foreach (var started in rows.Where(x => !x.Completed && x.OriginCompleted))
    foreach (
      var later in rows.Where(x =>
        ReferenceEquals(x.Truck, started.Truck)
        && x != started
        && !x.OriginCompleted
      )
    )
      if (due[later].CompareTo(due[started]) < 0)
        due[later] = due[started];
    for (var i = 0; i < 3; i++)
    {
      _columns[i] = rows.Where(x => x.Column(_today) == i)
        .OrderBy(x => due[x])
        .ThenBy(x => x.OriginCompleted ? 0 : 1)
        .ThenBy(x => x.Load.LoadNumber)
        .ToList();
    }
  }

  private (DateOnly, TimeOnly) Due(DispatchBoardRow row) =>
    (
      (ShowDelivery(row) ? row.DeliveryDate : row.PickupDate)
        ?? DateOnly.MaxValue,
      (
        ShowDelivery(row)
          ? row.Destination?.ScheduledTime
          : row.Origin?.ScheduledTime
      ) ?? TimeOnly.MaxValue
    );
}
