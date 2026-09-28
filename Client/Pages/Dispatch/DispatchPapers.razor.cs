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
    string?
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

  // The event a folder is about is the load's next stop not yet done
  // (the owner, September 27): its pickup until that is done, then any stop
  // between, then its delivery - never a stop already done, and never a
  // guess from which dates fall today or tomorrow.
  private static string EventLabel(DispatchBoardRow row) =>
    row.NextStop?.Job switch
    {
      "Pick Up" or "Pickup" => "Pickup",
      "Drop Off" or "Delivery" => "Delivery",
      var job => DispatchBoardRow.Text(job, "Stop"),
    };

  private static string EventSchedule(DispatchBoardRow row) =>
    DispatchBoardRow.Schedule(row.NextStop, EventFallback(row));

  // The load's own ship or delivery date stands in for a first or last
  // stop that has none.
  private static DateOnly? EventFallback(DispatchBoardRow row) =>
    row.NextStop is not { } stop ? null
    : stop == row.Origin ? row.Load.ShipDate
    : stop == row.Destination ? row.Load.DeliveryDate
    : null;

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
      _columns[i] = rows.Where(x => ColumnOf(x) == i)
        .OrderBy(x => due[x])
        .ThenBy(x => x.OriginCompleted ? 0 : 1)
        .ThenBy(x => x.Load.LoadNumber)
        .ToList();
    }
  }

  // The day a folder is due: its next stop not yet done, by that stop's
  // own local appointment date (or the load's ship or delivery date for an
  // end stop that has none) - never an ETA, never a guess at which load
  // the truck works now (the owner, September 28).
  private enum Day
  {
    Overdue,
    Today,
    Tomorrow,
    Later,
    Undated,
  }

  private static readonly string[] DayTitles =
  [
    "Overdue",
    "Today",
    "Tomorrow",
    "Later",
    "Date pending",
  ];

  private Day DayOf(DispatchBoardRow row) =>
    (row.NextStop?.ScheduledDate ?? EventFallback(row)) is not { } date
      ? Day.Undated
      : (date.DayNumber - _today.DayNumber) switch
      {
        < 0 => Day.Overdue,
        0 => Day.Today,
        1 => Day.Tomorrow,
        _ => Day.Later,
      };

  // In transit keeps its folder; otherwise a load whose next stop is due
  // by tomorrow, overdue included, is in the today-and-tomorrow folder.
  private int ColumnOf(DispatchBoardRow row) =>
    row.Column(_today) == 1 ? 1
    : row.Planned ? 0
    : DayOf(row) <= Day.Tomorrow ? 2
    : 0;

  // A folder's loads by day, today above tomorrow; In transit is one list.
  private IEnumerable<(string? Title, List<DispatchBoardRow> Rows)> Days(
    int column
  ) =>
    column == 1
      ? [(null, _columns[column])]
      : _columns[column]
        .GroupBy(DayOf)
        .OrderBy(x => x.Key)
        .Select(x => ((string?)DayTitles[(int)x.Key], x.ToList()));

  private static string EventTone(DispatchBoardRow row) =>
    EventLabel(row) switch
    {
      "Pickup" => "is-pickup",
      "Delivery" => "is-delivery",
      _ => "is-stop",
    };

  private static (DateOnly, TimeOnly) Due(DispatchBoardRow row) =>
    (
      row.NextStop?.ScheduledDate ?? EventFallback(row) ?? DateOnly.MaxValue,
      row.NextStop?.ScheduledTime ?? TimeOnly.MaxValue
    );
}
