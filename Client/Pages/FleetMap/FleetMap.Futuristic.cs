using System.Globalization;
using Client.Models.DTO;
using Client.Models.DTO.Dispatch;
using Client.Models.DTO.Planning;
using Client.Shared.Appearance.AppearanceProvider;
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;

namespace Client.Pages.FleetMap;

// The Futuristic interface adds a truck list and a trip chain around the
// same map. Both only route into the page's existing selection paths, so
// selection, Follow and the inspector behave as in the current interface.
public partial class FleetMap
{
  [CascadingParameter]
  private AppearanceProvider? Appearance { get; set; }

  private bool Futuristic => Appearance?.Futuristic == true;

  // The chain is the selected truck's row of the Dispatch board: the same
  // server read, order and work phase (DispatchResponse.WorkPhase) the
  // board shows, asked for one truck. Read when the truck changes and at
  // the board's own one-minute cadence; a late answer for another truck is
  // dropped.
  private static readonly TimeSpan ChainRefreshInterval = TimeSpan.FromMinutes(
    1
  );
  private IReadOnlyList<DispatchResponse> _chainLoads = [];
  private Guid? _chainTruckId;
  private DateTimeOffset _chainReadAt;
  private bool _chainLoading;
  private bool _chainFailed;
  private CancellationTokenSource? _chainRequest;

  private async Task RefreshChainIfDueAsync()
  {
    if (_disposed || !Futuristic)
      return;
    var truck = _activeTruckId;
    if (truck is null)
    {
      if (_chainTruckId is null)
        return;
      _chainRequest?.Cancel();
      _chainTruckId = null;
      _chainLoads = [];
      _chainLoading = _chainFailed = false;
      StateHasChanged();
      return;
    }
    if (
      truck == _chainTruckId
      && (
        _chainRequest is not null
        || Clock.GetUtcNow() - _chainReadAt < ChainRefreshInterval
      )
    )
      return;
    var changed = truck != _chainTruckId;
    _chainRequest?.Cancel();
    using var request = CancellationTokenSource.CreateLinkedTokenSource(
      _lifetime.Token
    );
    _chainRequest = request;
    _chainTruckId = truck;
    _chainReadAt = Clock.GetUtcNow();
    if (changed)
    {
      _chainLoads = [];
      _chainLoading = true;
      _chainFailed = false;
      StateHasChanged();
    }
    var date = DateOnly
      .FromDateTime(Clock.GetLocalNow().DateTime)
      .ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
    try
    {
      var result = await Api.GetAsync<
        PaginatedListDTO<TruckDispatchBoardResponse>
      >(
        $"api/dispatch/board?page=1&pageSize=12&search=&date={date}"
          + $"&truckId={truck}&includePlanned=true"
          + "&includeHos=false&includeFinancials=false&includeEta=false",
        request.Token
      );
      if (
        _disposed
        || request.IsCancellationRequested
        || _chainTruckId != truck
      )
        return;
      _chainLoading = false;
      _chainFailed = !result.Success || result.Response is null;
      if (!_chainFailed)
        _chainLoads =
          result
            .Response!.Items.FirstOrDefault(row => row.TruckId == truck)
            ?.Dispatches ?? [];
      StateHasChanged();
    }
    catch (OperationCanceledException) when (request.IsCancellationRequested)
    { }
    finally
    {
      if (ReferenceEquals(_chainRequest, request))
        _chainRequest = null;
    }
  }

  private void DisposeChain()
  {
    _chainRequest?.Cancel();
    _chainRequest = null;
  }

  // As choosing a truck in search: focus it on the map, then select it.
  private async Task ChooseListTruckAsync(Guid truckId)
  {
    if (_map is null || _disposed)
      return;
    if (truckId == _activeTruckId)
    {
      await SelectRouteAsync(truckId, _activeDispatchId);
      return;
    }
    if (
      await _map.InvokeAsync<bool>("focusTruck", truckId.ToString(), (int?)null)
    )
      await SelectRouteAsync(truckId, null);
  }

  // Back to the truck and its current load; the camera does not move, so an
  // active Follow continues.
  private Task ChooseCurrentLoadAsync() =>
    _activeTruckId is { } truck
      ? SelectRouteAsync(truck, _activeDispatchId)
      : Task.CompletedTask;

  // A later load opens its stop card through the map, the path a click on
  // its stop takes; it can only do so while its road is drawn.
  private async Task ChooseChainLoadAsync(DispatchResponse load)
  {
    if (_map is null || _disposed)
      return;
    if (load.Id == SelectedDispatchId)
    {
      await ChooseCurrentLoadAsync();
      return;
    }
    await _map.InvokeVoidAsync(
      "selectNextStop",
      load.Id.ToString(),
      0,
      load.ExecutionLegId?.ToString()
    );
  }

  private async Task ShowNextLoadsFromChainAsync()
  {
    if (ShowNextLoads)
      return;
    ShowNextLoads = true;
    await OnNextLoadsChanged();
  }
}
