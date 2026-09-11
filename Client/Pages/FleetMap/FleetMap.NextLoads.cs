using Client.Models.DTO.Planning;
using Client.Services;
using Microsoft.JSInterop;
using System.Text.Json;

namespace Client.Pages.FleetMap;

public partial class FleetMap
{
  private static readonly JsonSerializerOptions MapJsonOptions = new(JsonSerializerDefaults.Web);
  private bool ShowNextLoads { get; set; }
  private int _nextLoadsVersion;
  private string? _nextLoadsMessage;
  private string? _nextLoadsRevision;
  private (Guid Truck, Guid? Dispatch)? _nextLoadsIdentity;
  private CancellationTokenSource? _nextLoadsRequest;
  private readonly NextLoadDisplayCache _nextLoadsCache = new();
  private IReadOnlyList<NextLoadRoute> _nextLoadRoutes = [];

  private void ResetNextLoads()
  {
    ++_nextLoadsVersion;
    _nextLoadsRequest?.Cancel();
    _nextLoadsIdentity = null;
    _nextLoadsRevision = null;
    _nextLoadsMessage = null;
    _nextLoadRoutes = [];
    ResetInspectedLoad();
  }

  private async Task OnNextLoadsChanged()
  {
    ++_nextLoadsVersion;
    _nextLoadsRequest?.Cancel();
    _nextLoadsMessage = null;
    if (!ShowNextLoads) ResetInspectedLoad();
    if (_map is null || _disposed) return;
    await _map.InvokeVoidAsync("setNextLoadsVisible", ShowNextLoads);
    await RefreshNextLoadsAsync();
  }

  private async Task RefreshNextLoadsAsync()
  {
    if (_map is null || _disposed || !ShowNextLoads || _activeTruckId is not { } truckId) return;
    var currentId = SelectedDispatchId;
    if (_nextLoadsRequest is { IsCancellationRequested: false } && _nextLoadsIdentity == (truckId, currentId)) return;
    _nextLoadsRequest?.Cancel();
    var version = ++_nextLoadsVersion;
    using var request = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
    _nextLoadsRequest = request;
    try
    {
      if (_nextLoadsIdentity != (truckId, currentId))
      {
        _nextLoadRoutes = [];
        ResetInspectedLoad();
        _nextLoadsIdentity = (truckId, currentId);
        _nextLoadsRevision = null;
        await _map.InvokeVoidAsync("clearNextLoads");
        if (!IsCurrentNextLoads(version, truckId, currentId)) return;
        if (currentId is { } dispatchId && _nextLoadsCache.Get((truckId, dispatchId), Clock.GetUtcNow()) is { } cached)
        {
          if (!IsCurrentNextLoads(version, truckId, currentId)) return;
          using var savedJson = JsonDocument.Parse(cached.Payload);
          RememberNextLoadRoutes(savedJson.RootElement.GetProperty("routes").Deserialize<List<NextLoadRoute>>(MapJsonOptions) ?? []);
          await _map.InvokeVoidAsync("setNextLoadsBytes", cached.Payload);
          if (!IsCurrentNextLoads(version, truckId, currentId)) return;
          _nextLoadsRevision = cached.Revision;
        }
      }
      if (currentId is null)
      {
        _nextLoadsMessage = null;
        return;
      }
      if (!IsCurrentNextLoads(version, truckId, currentId)) return;
      var response = await Api.GetAsync<NextLoadRoutesResponse>(
        $"api/dispatch/truck/{truckId}/next-routes?currentDispatchId={currentId}&revision={Uri.EscapeDataString(_nextLoadsRevision ?? "")}", request.Token);
      if (_disposed || version != _nextLoadsVersion || truckId != _activeTruckId || currentId != SelectedDispatchId) return;
      if (!response.Success || response.Response is null)
      {
        _nextLoadsMessage = "Next load routes could not be loaded.";
        return;
      }
      _nextLoadsMessage = null;
      if (response.Response.Unchanged)
      {
        if (_nextLoadsCache.Get((truckId, currentId.Value), Clock.GetUtcNow()) is { } saved
          && saved.Revision == response.Response.Revision)
          _nextLoadsCache.Store((truckId, currentId.Value), saved.Revision, saved.Payload, Clock.GetUtcNow());
        return;
      }
      if (response.Response.Routes is null && response.Response.Labels is null) return;
      var upcoming = response.Response.Routes?.Where(x => x.Id != currentId).ToList();
      using var buffer = new ResponsiveWriteStream();
      await JsonSerializer.SerializeAsync(buffer, new { Routes = upcoming, response.Response.Labels,
        TruckId = truckId, CurrentDispatchId = currentId }, MapJsonOptions, request.Token);
      var payload = buffer.ToArray();
      var snapshot = upcoming is not null ? payload
        : await MergeNextLoadLabelsAsync(truckId, currentId.Value, response.Response.Labels, request.Token);
      if (!_disposed && version == _nextLoadsVersion && truckId == _activeTruckId && currentId == SelectedDispatchId)
      {
        if (upcoming is not null) RememberNextLoadRoutes(upcoming);
        await _map.InvokeVoidAsync("setNextLoadsBytes", payload);
        if (IsCurrentNextLoads(version, truckId, currentId))
        {
          _nextLoadsRevision = response.Response.Revision;
          if (snapshot is not null)
            _nextLoadsCache.Store((truckId, currentId.Value), response.Response.Revision, snapshot, Clock.GetUtcNow());
        }
      }
    }
    catch (OperationCanceledException) when (request.IsCancellationRequested) { }
    finally { if (ReferenceEquals(_nextLoadsRequest, request)) _nextLoadsRequest = null; }
  }

  private bool IsCurrentNextLoads(int version, Guid truckId, Guid? currentId) =>
    !_disposed && version == _nextLoadsVersion && truckId == _activeTruckId && currentId == SelectedDispatchId;

  private async Task<byte[]?> MergeNextLoadLabelsAsync(Guid truckId, Guid dispatchId,
    IReadOnlyList<NextLoadLabels>? labels, CancellationToken cancellationToken)
  {
    if (_nextLoadsCache.Get((truckId, dispatchId), Clock.GetUtcNow()) is not { } cached) return null;
    using var json = JsonDocument.Parse(cached.Payload);
    using var buffer = new ResponsiveWriteStream();
    // Cache complete geometry, but keep the live interop update metadata-only.
    await JsonSerializer.SerializeAsync(buffer, new { Routes = json.RootElement.GetProperty("routes"), Labels = labels,
      TruckId = truckId, CurrentDispatchId = dispatchId },
      MapJsonOptions, cancellationToken);
    return buffer.ToArray();
  }

  private void RememberNextLoadRoutes(IReadOnlyList<NextLoadRoute> routes)
  {
    _nextLoadRoutes = routes.Select(route => route with
    {
      Legs = route.Legs.Select(leg => leg with { Points = [] }).ToArray(),
      Deadhead = route.Deadhead is { } deadhead ? deadhead with { Points = [] } : null
    }).ToArray();
    if (_inspectedLoadId is { } id && !_nextLoadRoutes.Any(route => route.Id == id)) ResetInspectedLoad();
  }
}
