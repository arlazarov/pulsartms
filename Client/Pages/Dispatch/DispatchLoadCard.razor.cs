using Client.Models.DTO.Dispatch;
using Client.Shared.Dispatch;
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;

namespace Client.Pages.Dispatch;

public partial class DispatchLoadCard
{
    [CascadingParameter] public Client.Models.DTO.DispatchSettingsState? DisplaySettings { get; set; }
    [Inject] private IJSRuntime JS { get; set; } = default!;
    [Parameter, EditorRequired] public DispatchResponse Load { get; set; } = default!;
    [Parameter] public TruckDispatchBoardResponse? Truck { get; set; }
    [Parameter] public bool Current { get; set; }
    [Parameter] public int Order { get; set; }
    [Parameter] public bool Refreshing { get; set; }
    [Parameter] public string HeaderDriverName { get; set; } = "";
    [Parameter] public string HeaderTrailerNumber { get; set; } = "";
    [Parameter] public double? RemainingMiles { get; set; }
    [Parameter] public int? FuelStopCount { get; set; }
    private string LoadLabel => Client.Shared.Dispatch.LoadNumberDisplay.Format(Load.LoadNumber, DisplaySettings?.LoadNumberPrefix);
    private bool Completed => DispatchBoardRow.IsCompleted(Load);
    private bool Next => !Current && !Completed && Order <= 1;
    private bool ShowRemaining => Current && !Completed && RemainingMiles is { } miles && double.IsFinite(miles) && miles >= 0;
    private bool ShowFuelStops => !Completed && FuelStopCount is >= 0;
    private string Phase => Completed ? "Completed" : Current ? "Current" : Next ? "Next" : "Upcoming";
    private string Status => new DispatchBoardRow(new(), Load).Status;
    private bool ShowDriverAssignment => HasDifferentAssignment(Load.DriverName, HeaderDriverName);
    private bool ShowTrailerAssignment => HasDifferentAssignment(Load.TrailerNumber, HeaderTrailerNumber);
    private static bool HasDifferentAssignment(string? value, string? header) => !string.IsNullOrWhiteSpace(value)
        && !string.Equals(value.Trim(), header?.Trim(), StringComparison.OrdinalIgnoreCase);
    private List<DispatchStopResponse> OrderedStops { get; set; } = [];
    private (Guid Load, string Order) _copyIdentity;
    private bool _copied, _showDetails;
    private string? _copyError;
    private void OpenDetails() => _showDetails = true;
    private void CloseDetails() => _showDetails = false;
    protected override void OnParametersSet()
    {
        OrderedStops = Load.Stops.OrderBy(stop => stop.Sequence).ToList();
        var identity = (Load.Id, Load.OrderNumber);
        if (_copyIdentity.Load != Load.Id) _showDetails = false;
        if (_copyIdentity != identity) { _copied = false; _copyError = null; }
        _copyIdentity = identity;
    }
    private async Task CopyOrderAsync()
    {
        var identity = _copyIdentity;
        if (string.IsNullOrWhiteSpace(identity.Order)) return;
        try
        {
            await JS.InvokeVoidAsync("navigator.clipboard.writeText", identity.Order);
            if (_copyIdentity == identity) { _copied = true; _copyError = null; }
        }
        catch (JSException)
        {
            if (_copyIdentity == identity) { _copied = false; _copyError = "Could not copy. Please try again."; }
        }
    }
    private DateOnly? FallbackDate(DispatchStopResponse stop) => stop == OrderedStops.FirstOrDefault()
        ? Load.ShipDate : stop == OrderedStops.LastOrDefault() ? Load.DeliveryDate : null;
}
