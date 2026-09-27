using System.Globalization;
using Client.Models;
using Client.Models.DTO;
using Client.Models.DTO.Dispatch;
using Client.Services;
using Client.Shared.Dispatch;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;

namespace Client.Pages.Dispatch;

public partial class DispatchTable
{
  [Inject]
  private NavigationManager Navigation { get; set; } = default!;

  [CascadingParameter]
  public DispatchSettingsState? DisplaySettings { get; set; }

  [CascadingParameter]
  public DisplayUnits Units { get; set; } = DisplayUnits.Default;

  // The list's own address, so the load page can return to it.
  [Parameter]
  public string? ReturnOrigin { get; set; }

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

  private string LoadLabel(int number) =>
    LoadNumberDisplay.Format(number, DisplaySettings?.LoadNumberPrefix);

  private string Phase(DispatchBoardRow row) =>
    Completed || row.Completed
      ? "Completed"
      : LoadPhase?.Invoke(row.Truck, row.Load)
        ?? (row.Planned ? "Planned" : "");

  // The rows by the day each load is picked up: the earliest day at the
  // top and the future below it (the owner, September 27); history reads
  // from its latest day. A load without a date closes the list. Each day
  // is a card whose colour says past, today or future, and today always
  // has one, so the eye finds where the plan starts.
  private sealed record DispatchDay(
    DateOnly? Date,
    string Tone,
    string Weekday,
    string Number,
    string Label,
    string? When,
    IReadOnlyList<TruckRun> Runs
  )
  {
    public int Count => Runs.Sum(run => run.Rows.Count);
  }

  // The loads one truck picks up that day, as for LTL: named once above
  // them when there is more than one. The truck is its identity, never its
  // displayed number, so trucks without one are never merged; a driver or
  // trailer is named above only when every load shares it, since the truck
  // can change either during the day.
  private sealed record TruckRun(IReadOnlyList<DispatchBoardRow> Rows)
  {
    public DispatchBoardRow First => Rows[0];
    public string? Driver => Common(row => row.DriverName);
    public string? Trailer => Common(row => row.TrailerNumber);

    private string? Common(Func<DispatchBoardRow, string> fact) =>
      Rows.Select(fact).Distinct().Count() == 1 ? fact(First) : null;
  }

  private IReadOnlyList<DispatchBoardRow> _rows = [];
  private IReadOnlyList<DispatchDay> _days = [];

  protected override void OnParametersSet()
  {
    var today = DateOnly.FromDateTime(DateTime.Today);
    _rows = Trucks
      .SelectMany(truck =>
        truck.Dispatches.Select(load => new DispatchBoardRow(truck, load))
      )
      .ToArray();
    var days = _rows
      .GroupBy(row => row.PickupDate)
      .Select(day => Day(day.Key, today, Runs(day)))
      .ToList();
    if (!Completed && days.Count > 0 && days.All(day => day.Date != today))
      days.Add(Day(today, today, []));
    _days = Completed
      ? days.OrderBy(day => day.Date is null)
        .ThenByDescending(day => day.Date)
        .ToArray()
      : days.OrderBy(day => day.Date is null).ThenBy(day => day.Date).ToArray();
  }

  // Within a day, the first pickup first, and of two picked up alike the
  // first delivery first (the owner, September 27); an unknown time goes
  // last, and loads booked alike keep the board's order, so a truck's
  // current load stays ahead of its next. A truck is named once over its
  // loads only where they follow each other in that order, so the order
  // is never bent to keep a truck's loads together.
  private static IReadOnlyList<TruckRun> Runs(IEnumerable<DispatchBoardRow> day)
  {
    var runs = new List<List<DispatchBoardRow>>();
    foreach (
      var row in day.OrderBy(row =>
          row.Origin?.ScheduledTime ?? TimeOnly.MaxValue
        )
        .ThenBy(row => row.DeliveryDate ?? DateOnly.MaxValue)
        .ThenBy(row => row.Destination?.ScheduledTime ?? TimeOnly.MaxValue)
    )
    {
      var last = runs.LastOrDefault();
      if (
        last is not null
        && row.TruckIdentity is { } truck
        && last[0].TruckIdentity == truck
      )
        last.Add(row);
      else
        runs.Add([row]);
    }
    return runs.Select(run => new TruckRun(run)).ToArray();
  }

  private static DispatchDay Day(
    DateOnly? day,
    DateOnly today,
    IReadOnlyList<TruckRun> runs
  )
  {
    if (day is not { } value)
      return new(null, "is-undated", "", "—", "No pickup date", null, runs);
    var offset = value.DayNumber - today.DayNumber;
    return new(
      value,
      offset < 0 ? "is-past"
        : offset == 0 ? "is-today"
        : "is-future",
      value.ToString("ddd", CultureInfo.InvariantCulture).ToUpperInvariant(),
      value.Day.ToString(CultureInfo.InvariantCulture),
      value.ToString("ddd, MMM d", CultureInfo.InvariantCulture),
      offset switch
      {
        0 => "Today",
        -1 => "Yesterday",
        1 => "Tomorrow",
        < 0 => $"{-offset} days ago",
        _ => $"In {offset} days",
      },
      runs
    );
  }

  private static string Loads(int count) =>
    count == 1 ? "1 load" : $"{count} loads";

  private string LoadUrl(DispatchBoardRow row) =>
    ReturnNavigation.Load(row.Load.Id, ReturnOrigin);

  private void OpenFromRow(DispatchBoardRow row, MouseEventArgs e)
  {
    if (e.Button == 0 && !e.CtrlKey && !e.MetaKey && !e.ShiftKey && !e.AltKey)
      Navigation.NavigateTo(LoadUrl(row));
  }

  private static IReadOnlyList<DispatchStopVisit> Stops(
    IReadOnlyList<DispatchStopVisit> visits,
    bool pickup
  ) => visits.Where(visit => IsDelivery(visit.Stop) != pickup).ToArray();

  private static DispatchStopVisit? SummaryVisit(
    DispatchBoardRow row,
    IReadOnlyList<DispatchStopVisit> stops
  ) =>
    stops.FirstOrDefault(visit => !StopCompleted(row, visit.Stop))
    ?? stops.LastOrDefault();

  private static string StopSummary(
    DispatchBoardRow row,
    IReadOnlyList<DispatchStopVisit> stops,
    bool pickup
  )
  {
    var label = pickup
      ? stops.All(visit => IsPickup(visit.Stop))
        ? "pickups"
        : "pickup / other stops"
      : "deliveries";
    return $"{stops.Count} {label} · {stops.Count(visit => StopCompleted(row, visit.Stop))} completed";
  }

  private bool HasOtherStops =>
    _rows.Any(row =>
      row.Load.Stops.Any(stop => !IsPickup(stop) && !IsDelivery(stop))
    );

  private static bool IsDelivery(DispatchStopResponse stop) =>
    DispatchStopPresentation.IsDelivery(stop);

  private static bool IsPickup(DispatchStopResponse stop) =>
    DispatchStopPresentation.IsPickup(stop);

  private static bool StopCompleted(
    DispatchBoardRow row,
    DispatchStopResponse stop
  ) => !stop.DriverOnly && row.Completed || stop.IsCompleted;
}
