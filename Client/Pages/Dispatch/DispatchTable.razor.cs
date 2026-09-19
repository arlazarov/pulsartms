using Client.Models.DTO;
using Client.Models.DTO.Dispatch;
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

  private IEnumerable<DispatchBoardRow> Rows =>
    Trucks.SelectMany(truck =>
      truck.Dispatches.Select(load => new DispatchBoardRow(truck, load))
    );

  private static string LoadUrl(DispatchBoardRow row) =>
    $"/dispatch/{row.Load.Id}";

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
    Rows.Any(row =>
      row.Load.Stops.Any(stop => !IsPickup(stop) && !IsDelivery(stop))
    );

  private static bool IsDelivery(DispatchStopResponse stop) =>
    stop.Job.Equals("Drop Off", StringComparison.OrdinalIgnoreCase)
    || stop.Job.Equals("Delivery", StringComparison.OrdinalIgnoreCase);

  private static bool IsPickup(DispatchStopResponse stop) =>
    stop.Job.Equals("Pick Up", StringComparison.OrdinalIgnoreCase)
    || stop.Job.Equals("Pickup", StringComparison.OrdinalIgnoreCase);

  private static bool StopCompleted(
    DispatchBoardRow row,
    DispatchStopResponse stop
  ) => !stop.DriverOnly && row.Completed || stop.IsCompleted;
}
