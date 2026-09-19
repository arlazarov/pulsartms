using Client.Models.DTO.Dispatch.Workspace;
using Client.Models.DTO.Mileage;
using Client.Services;
using Microsoft.AspNetCore.Components;

namespace Client.Pages.Dispatch;

public partial class LoadAdjustments : IDisposable
{
  [Inject]
  private ApiService Api { get; set; } = default!;

  [Parameter, EditorRequired]
  public DispatchWorkspaceMetadata Metadata { get; set; } = new();

  [Parameter]
  public LoadBillingTotals? Totals { get; set; }

  [Parameter]
  public string Currency { get; set; } = "";

  [Parameter]
  public bool CanEdit { get; set; }

  [Parameter]
  public EventCallback Changed { get; set; }
  private readonly CancellationTokenSource _lifetime = new();
  private List<MileageDriverOption> _drivers = [];
  private bool _loading;
  private bool _disposed;
  private string? _error;

  private Task NotifyChanged() => Changed.InvokeAsync();

  private async Task AddAsync()
  {
    if (!CanEdit || Metadata.Adjustments.Count >= 100)
      return;
    Metadata.Adjustments.Add(
      new() { Id = Guid.NewGuid(), Currency = Metadata.Currency }
    );
    await NotifyChanged();
  }

  private async Task RemoveAsync(LoadAdjustment row)
  {
    if (!CanEdit)
      return;
    Metadata.Adjustments.Remove(row);
    await NotifyChanged();
  }

  private async Task RecipientAsync(LoadAdjustment row)
  {
    row.DriverId = null;
    row.DriverName = "";
    await NotifyChanged();
    if (row.Target == "driver" && _drivers.Count == 0)
      await LoadDriversAsync();
  }

  private async Task LoadDriversAsync()
  {
    if (_loading || !CanEdit)
      return;
    _loading = true;
    _error = null;
    var result = await Api.GetAsync<MileageFleetList<MileageDriverOption>>(
      "api/fleet/drivers",
      _lifetime.Token
    );
    if (_disposed)
      return;
    _loading = false;
    if (result.Success && result.Response is { } response)
      _drivers = response.Items;
    else
      _error = result.ErrorMessage ?? "Could not load drivers.";
  }

  public void Dispose()
  {
    _disposed = true;
    _lifetime.Cancel();
    _lifetime.Dispose();
  }
}
