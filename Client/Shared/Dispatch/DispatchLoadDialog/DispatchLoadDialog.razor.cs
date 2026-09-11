using Client.Models.DTO.Dispatch;
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;

namespace Client.Shared.Dispatch.DispatchLoadDialog;

public partial class DispatchLoadDialog
{
    [Inject] private IJSRuntime JS { get; set; } = default!;
    [Parameter, EditorRequired] public DispatchResponse Load { get; set; } = default!;
    [Parameter] public TruckDispatchBoardResponse? Truck { get; set; }
    [Parameter] public string Phase { get; set; } = "";
    [Parameter] public bool Completed { get; set; }
    [Parameter] public bool Refreshing { get; set; }
    [Parameter] public EventCallback Closed { get; set; }
    private ElementReference _dialog;
    private IJSObjectReference? _module;
    private bool _disposed, _closed;
    private DispatchBoardRow Row => new(Truck ?? new(), Load);
    private bool IsCompleted => Completed || DispatchBoardRow.IsCompleted(Load);
    private string DisplayPhase => IsCompleted ? "Completed" : Phase;
    private string DialogId => $"dispatch-load-dialog-{Load.Id}";
    private string TitleId => $"{DialogId}-title";
    private string? EmptyMilesHint => Load.EmptyMilesStatus == "ready"
        ? "Planned road miles from the previous load's delivery to this pickup" : null;
    private List<DispatchStopResponse> OrderedStops { get; set; } = [];

    protected override void OnParametersSet() => OrderedStops = Load.Stops.OrderBy(stop => stop.Sequence).ToList();

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (!firstRender || _disposed) return;
        var module = await JS.InvokeAsync<IJSObjectReference>("import", "./js/generated/shared/loadDialog.js");
        if (_disposed) { await module.DisposeAsync(); return; }
        _module = module;
        await module.InvokeVoidAsync("show", _dialog);
    }

    private async Task CloseAsync()
    {
        if (_module is not null) await _module.InvokeVoidAsync("close", _dialog);
        await NotifyClosedAsync();
    }

    private async Task NotifyClosedAsync()
    {
        if (_disposed || _closed) return;
        _closed = true;
        await Closed.InvokeAsync();
    }

    private DateOnly? FallbackDate(int index) => index == 0 ? Load.ShipDate
        : index == OrderedStops.Count - 1 ? Load.DeliveryDate : null;
    private static bool IsPickup(DispatchStopResponse stop) => stop.Job.Equals("Pick Up", StringComparison.OrdinalIgnoreCase)
        || stop.Job.Equals("Pickup", StringComparison.OrdinalIgnoreCase);

    public async ValueTask DisposeAsync()
    {
        _disposed = true;
        if (_module is null) return;
        try { await _module.InvokeVoidAsync("dispose", _dialog); }
        catch (JSDisconnectedException) { }
        try { await _module.DisposeAsync(); }
        catch (JSDisconnectedException) { }
    }
}
