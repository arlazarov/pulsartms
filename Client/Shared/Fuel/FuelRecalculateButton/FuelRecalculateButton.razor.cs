using Client.Models.DTO.Planning;
using Client.Services;
using Microsoft.AspNetCore.Components;

namespace Client.Shared.Fuel.FuelRecalculateButton;

public partial class FuelRecalculateButton : IDisposable
{
    [Inject] private ApiService Api { get; set; } = default!;
    [Inject] private PlanningDisplayCache PlanningCache { get; set; } = default!;
    [Inject] private ILogger<FuelRecalculateButton> Logger { get; set; } = default!;
    [Parameter] public Guid DispatchId { get; set; }
    [Parameter] public bool IconOnly { get; set; }
    [Parameter] public bool ManuallyEdited { get; set; }
    [Parameter] public bool Disabled { get; set; }
    [Parameter] public EventCallback EditRequested { get; set; }
    [Parameter] public EventCallback<AutomaticPlanningResult> Recalculated { get; set; }
    [Parameter] public EventCallback<bool> BusyChanged { get; set; }
    [Parameter] public EventCallback Failed { get; set; }
    private readonly CancellationTokenSource _lifetime = new();
    private bool _busy;
    private bool _disposed;

    private async Task RecalculateAsync()
    {
        if (_busy || _disposed || Disabled || DispatchId == Guid.Empty) return;
        if (ManuallyEdited) { await EditRequested.InvokeAsync(); return; }
        _busy = true;
        await BusyChanged.InvokeAsync(true);
        try
        {
            var response = await Api.PostAsync<object, AutomaticPlanningResult>(
                $"api/dispatch/{DispatchId}/planning/fuel/recalculate", new { }, _lifetime.Token);
            if (_disposed) return;
            if (response.Success && response.Response is { } result)
            {
                PlanningCache.StoreRecalculated(result);
                await Recalculated.InvokeAsync(result);
            }
            else
            {
                var reasons = response.Errors?.Where(reason => !string.IsNullOrWhiteSpace(reason)).ToArray();
                Logger.LogWarning("Calculate Fuel failed for dispatch {DispatchId}: {Reason}", DispatchId,
                    reasons is { Length: > 0 } ? string.Join("; ", reasons) : "The server returned no fuel calculation result.");
                await Failed.InvokeAsync();
            }
        }
        catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException or System.Text.Json.JsonException)
        {
            if (!_disposed)
            {
                Logger.LogWarning(ex, "Calculate Fuel failed for dispatch {DispatchId}: {Reason}", DispatchId, ex.Message);
                await Failed.InvokeAsync();
            }
        }
        finally
        {
            _busy = false;
            if (!_disposed) await BusyChanged.InvokeAsync(false);
        }
    }

    public void Dispose() { _disposed = true; _lifetime.Cancel(); _lifetime.Dispose(); }
}
