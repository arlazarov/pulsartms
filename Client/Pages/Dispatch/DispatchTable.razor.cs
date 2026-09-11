using Client.Models.DTO.Dispatch;
using Client.Shared.Dispatch;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
namespace Client.Pages.Dispatch;
public partial class DispatchTable
{
  [CascadingParameter] public Client.Models.DTO.DispatchSettingsState? DisplaySettings { get; set; }
  [Parameter, EditorRequired] public IReadOnlyList<TruckDispatchBoardResponse> Trucks { get; set; } = [];
  [Parameter] public bool Completed { get; set; }
  [Parameter] public bool Refreshing { get; set; }
  [Parameter] public Func<TruckDispatchBoardResponse, DispatchResponse, string>? LoadPhase { get; set; }
  private string LoadLabel(int number) => Client.Shared.Dispatch.LoadNumberDisplay.Format(number, DisplaySettings?.LoadNumberPrefix);
  private string Phase(DispatchBoardRow row) => Completed || row.Completed ? "Completed"
    : LoadPhase?.Invoke(row.Truck, row.Load) ?? (row.Planned ? "Planned" : "");
  private IEnumerable<DispatchBoardRow> Rows => Trucks.SelectMany(truck => truck.Dispatches.Select(load => new DispatchBoardRow(truck, load)));
  private Guid? _selected;
  private bool _previousCompleted;
  private DispatchBoardRow? SelectedRow => Rows.FirstOrDefault(row => row.Load.Id == _selected);

  protected override void OnParametersSet()
  {
    if (_previousCompleted != Completed || SelectedRow is null) _selected = null;
    _previousCompleted = Completed;
  }

  private void Open(DispatchBoardRow row) => _selected = row.Load.Id;
  private void Close() => _selected = null;
  private void OpenFromRow(DispatchBoardRow row, MouseEventArgs e)
  {
    if (e.Button == 0 && !e.CtrlKey && !e.MetaKey && !e.ShiftKey && !e.AltKey) Open(row);
  }
  private static IReadOnlyList<DispatchStopResponse> Stops(DispatchBoardRow row, bool pickup) => row.Load.Stops
    .Where(stop => IsPickup(stop) == pickup).OrderBy(stop => stop.Sequence).ToArray();
  private static bool IsPickup(DispatchStopResponse stop) => stop.Job.Equals("Pick Up", StringComparison.OrdinalIgnoreCase)
    || stop.Job.Equals("Pickup", StringComparison.OrdinalIgnoreCase);
  private static bool StopCompleted(DispatchBoardRow row, DispatchStopResponse stop) => row.Completed
    || (stop.DepartedAt ?? stop.DeliveredAt ?? stop.PickedUpAt).HasValue;
}
