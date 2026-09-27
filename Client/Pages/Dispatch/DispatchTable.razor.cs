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
  // from its latest day. A load without a date closes the list.
  private sealed record DispatchDay(
    string Label,
    IReadOnlyList<DispatchBoardRow> Rows
  );

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
    var days = _rows.GroupBy(row => row.PickupDate);
    var ordered = Completed
      ? days.OrderBy(day => day.Key is null).ThenByDescending(day => day.Key)
      : days.OrderBy(day => day.Key is null).ThenBy(day => day.Key);
    _days = ordered
      .Select(day => new DispatchDay(
        DayLabel(day.Key, today),
        // A stable order: loads booked alike keep the board's order, so a
        // truck's current load stays ahead of its next.
        day.OrderBy(row => row.Origin?.ScheduledTime ?? TimeOnly.MaxValue)
          .ToArray()
      ))
      .ToArray();
  }

  private static string DayLabel(DateOnly? day, DateOnly today)
  {
    if (day is not { } value)
      return "No pickup date";
    var date = value.ToString("ddd, MMM d", CultureInfo.InvariantCulture);
    return (value.DayNumber - today.DayNumber) switch
    {
      -1 => $"Yesterday · {date}",
      0 => $"Today · {date}",
      1 => $"Tomorrow · {date}",
      _ => date,
    };
  }

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
