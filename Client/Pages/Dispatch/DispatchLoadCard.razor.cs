using Client.Models.DTO;
using Client.Models.DTO.Dispatch;
using Client.Services;
using Client.Shared.Dispatch;
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;

namespace Client.Pages.Dispatch;

public partial class DispatchLoadCard
{
  [Parameter]
  public EventCallback StopChanged { get; set; }

  [CascadingParameter]
  public DispatchSettingsState? DisplaySettings { get; set; }

  // The list's own address, so the load page can return to it.
  [Parameter]
  public string? ReturnOrigin { get; set; }

  [Inject]
  private IJSRuntime JS { get; set; } = default!;

  [Parameter, EditorRequired]
  public DispatchResponse Load { get; set; } = default!;

  [Parameter]
  public TruckDispatchBoardResponse? Truck { get; set; }

  [Parameter]
  public bool Current { get; set; }

  [Parameter]
  public int Order { get; set; }

  // A row ahead of the truck's current load on the board: planning has
  // moved past it, for a reason this card does not know, so it claims
  // no phase.
  [Parameter]
  public bool Earlier { get; set; }

  [Parameter]
  public bool Refreshing { get; set; }

  [Parameter]
  public double? RemainingMiles { get; set; }

  [Parameter]
  public int? FuelStopCount { get; set; }
  private string LoadLabel =>
    LoadNumberDisplay.Format(
      Load.LoadNumber,
      DisplaySettings?.LoadNumberPrefix
    );
  private bool Completed => Load.Completed;
  private bool Next => !Current && !Completed && !Earlier && Order <= 1;
  private bool ShowRemaining =>
    Current
    && !Completed
    && RemainingMiles is { } miles
    && double.IsFinite(miles)
    && miles >= 0;
  private bool ShowFuelStops => !Completed && FuelStopCount is >= 0;
  private string Phase =>
    Completed ? "Completed"
    : Earlier ? ""
    : Current ? "Current"
    : Next ? "Next"
    : "Upcoming";
  private string Status => new DispatchBoardRow(new(), Load).Status;

  private readonly DispatchStopDisplayCache _stopDisplay = new();
  private IReadOnlyList<DispatchStopResponse> OrderedStops =>
    _stopDisplay.OrderedStops;
  private IReadOnlyList<DispatchStopVisit> StopVisits => _stopDisplay.Visits;
  private string StopSummary => _stopDisplay.Summary;
  private int CompletedStopCount =>
    StopVisits.Count(visit =>
      visit.Stop.IsCompleted || Completed && !visit.Stop.DriverOnly
    );
  private (Guid Load, string Order) _copyIdentity;
  private bool _copied;
  private string? _copyError;
  private string LoadUrl => ReturnNavigation.Load(Load.Id, ReturnOrigin);

  private string StopUrl(Guid stopId) =>
    ReturnNavigation.Load(Load.Id, ReturnOrigin, stopId);

  protected override void OnParametersSet()
  {
    _stopDisplay.Update(Load.Stops);
    var identity = (Load.Id, Load.OrderNumber);
    if (_copyIdentity != identity)
    {
      _copied = false;
      _copyError = null;
    }
    _copyIdentity = identity;
  }

  private async Task CopyOrderAsync()
  {
    var identity = _copyIdentity;
    if (string.IsNullOrWhiteSpace(identity.Order))
      return;
    try
    {
      await JS.InvokeVoidAsync("navigator.clipboard.writeText", identity.Order);
      if (_copyIdentity == identity)
      {
        _copied = true;
        _copyError = null;
      }
    }
    catch (JSException)
    {
      if (_copyIdentity == identity)
      {
        _copied = false;
        _copyError = "Could not copy. Please try again.";
      }
    }
  }

  private DateOnly? FallbackDate(DispatchStopResponse stop) =>
    stop == OrderedStops.FirstOrDefault() ? Load.ShipDate
    : stop == OrderedStops.LastOrDefault() ? Load.DeliveryDate
    : null;
}
