using System.Text.Json;
using Client.Models.DTO.Planning;
using Client.Shared.Routing.RouteEditor;
using Microsoft.JSInterop;

namespace Client.Pages.FleetMap;

public partial class FleetMap
{
  private Task OpenSelectedRouteEditorAsync() =>
    SelectedDispatchId is { } id
      ? OpenRouteEditorAsync(id, SelectedExecutionLegId)
      : Task.CompletedTask;

  private Guid? _routeEditorDispatch;
  private Guid? _routeEditorExecutionLeg;
  private Guid _routeEditorSession;
  private RouteEditor? _routeEditor;
  private Guid? _publishedRouteEditorPreview;

  private async Task OpenRouteEditorAsync(Guid dispatch, Guid? legId = null)
  {
    if (
      _disposed
      || _map is null
      || dispatch == Guid.Empty
      || _fuelEditorOpen
      || _cameraOpen
    )
      return;
    _routeEditorDispatch = dispatch;
    _routeEditorExecutionLeg = legId;
    _routeEditorSession = Guid.NewGuid();
    _publishedRouteEditorPreview = null;
    await PublishInspectorSuspensionAsync();
    if (_activeTruckId is { } truck)
      await _map.InvokeVoidAsync("setFollow", truck.ToString(), false);
    _followingTruck = false;
    await _map.InvokeVoidAsync("clearMapInspection");
  }

  private async Task PublishRouteEditorAsync(RouteEditorMap payload)
  {
    if (
      _disposed
      || _map is null
      || payload.Session != _routeEditorSession
      || payload.Preview.DispatchId != _routeEditorDispatch
      || payload.Preview.ExecutionLegId != _routeEditorExecutionLeg
    )
      return;
    var update = new RouteEditorUpdate(
      payload.Session,
      payload.Preview.Id,
      payload.Selected,
      payload.Editing,
      payload.AddPoint,
      _publishedRouteEditorPreview == payload.Preview.Id
        ? null
        : payload.Preview
    );
    var bytes = JsonSerializer.SerializeToUtf8Bytes(
      update,
      new JsonSerializerOptions(JsonSerializerDefaults.Web)
    );
    await _map.InvokeVoidAsync("setRouteEditor", _lifetime.Token, bytes);
    if (!_disposed && payload.Session == _routeEditorSession)
      _publishedRouteEditorPreview = payload.Preview.Id;
  }

  private async Task CloseRouteEditorAsync()
  {
    _routeEditorDispatch = null;
    _routeEditorExecutionLeg = null;
    _routeEditorSession = Guid.Empty;
    _routeEditor = null;
    _publishedRouteEditorPreview = null;
    if (_map is not null && !_disposed)
      await _map.InvokeVoidAsync(
        "setRouteEditor",
        _lifetime.Token,
        (byte[]?)null
      );
    await BackToTruckAsync();
  }

  private async Task RouteChoiceSavedAsync()
  {
    var dispatch = _routeEditorDispatch;
    await CloseRouteEditorAsync();
    if (_activeTruckId is { } truck && dispatch is { } saved)
      PlanningCache.Invalidate(truck, saved);
    ResetNextLoads();
    if (dispatch == SelectedDispatchId)
      SetRouteState(null);
    await LoadRouteAsync(false, force: true);
    await RefreshNextLoadsAsync();
  }

  [JSInvokable]
  public Task OnRouteOptionSelected(string session, int option) =>
    Guid.TryParse(session, out var id)
    && id == _routeEditorSession
    && _routeEditor is not null
      ? _routeEditor.Select(option)
      : Task.CompletedTask;

  [JSInvokable]
  public Task OnRouteViaChanged(
    string session,
    string? via,
    int leg,
    double latitude,
    double longitude
  ) =>
    Guid.TryParse(session, out var id)
    && id == _routeEditorSession
    && _routeEditor is not null
      ? _routeEditor.PointChanged(via, leg, latitude, longitude)
      : Task.CompletedTask;
}
