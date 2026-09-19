using Client.Models.DTO.Dispatch;
using Client.Models.DTO.Dispatch.Workspace;
using Client.Models.DTO.Planning;
using Client.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;

namespace Client.Pages.Dispatch;

public partial class DispatchStopMap
{
  [Inject]
  private IJSRuntime JS { get; set; } = default!;

  [Inject]
  private IConfiguration Configuration { get; set; } = default!;

  [Inject]
  private ApiService Api { get; set; } = default!;

  [Parameter]
  public List<DispatchWorkspaceStop> Stops { get; set; } = [];

  [Parameter]
  public Guid? SelectedStopId { get; set; }

  [Parameter]
  public Guid? TruckId { get; set; }

  [Parameter]
  public DispatchResponse? Load { get; set; }

  [Parameter]
  public bool DraftChanged { get; set; }

  [Parameter]
  public string SourceFingerprint { get; set; } = "";

  private ElementReference _element;
  private Task<IJSObjectReference>? _moduleTask;
  private string? _published;
  private DispatchMapRoute? _publishedRoute;
  private Guid? _publishedSelection;
  private string? _error;
  private bool _disposed;
  private DispatchMapRoute? _route;
  private CancellationTokenSource? _read;
  private string? _readKey;
  private bool _routeLoading;
  private string? _routeError;
  private bool HasCoordinates =>
    Stops.Any(s =>
      s.Latitude is >= -90 and <= 90 && s.Longitude is >= -180 and <= 180
    );

  protected override async Task OnParametersSetAsync()
  {
    if (DraftChanged)
    {
      _read?.Cancel();
      _readKey = null;
      _routeLoading = false;
      return;
    }
    if (
      Load is null
      || !HasCoordinates
      || string.IsNullOrWhiteSpace(Configuration["GoogleMaps:ApiKey"])
    )
    {
      _read?.Cancel();
      _readKey = null;
      _route = null;
      _routeLoading = false;
      return;
    }
    var loadId = Load.Id;
    var key = System.Text.Json.JsonSerializer.Serialize(
      new
      {
        Load.Id,
        SourceFingerprint,
        stops = Stops.Select(x => new
        {
          x.Id,
          x.Sequence,
          x.Latitude,
          x.Longitude,
        }),
      }
    );
    if (_readKey == key)
      return;
    _read?.Cancel();
    _read?.Dispose();
    var owner = _read = new CancellationTokenSource();
    _readKey = key;
    _route = null;
    _routeError = null;
    _routeLoading = true;
    var result = await Api.GetAsync<DispatchMapRoute>(
      $"api/dispatch/{loadId}/planning/map",
      owner.Token
    );
    if (_disposed || owner.IsCancellationRequested || _read != owner)
      return;
    _routeLoading = false;
    if (result.Success && result.Response?.DispatchId == loadId)
      _route = result.Response;
    else
      _routeError = "Road route could not be loaded.";
  }

  private async Task RetryRouteAsync()
  {
    _readKey = null;
    await OnParametersSetAsync();
  }

  protected override async Task OnAfterRenderAsync(bool firstRender)
  {
    if (_disposed || _error is not null)
      return;
    var key = Configuration["GoogleMaps:ApiKey"];
    if (string.IsNullOrWhiteSpace(key))
    {
      _error = "Map is not configured.";
      StateHasChanged();
      return;
    }
    if (!HasCoordinates)
      return;
    var points = Stops
      .Where(s =>
        s.Latitude is >= -90 and <= 90 && s.Longitude is >= -180 and <= 180
      )
      .Select(s => new
      {
        id = s.Id,
        latitude = s.Latitude,
        longitude = s.Longitude,
        number = Stops.IndexOf(s) + 1,
        name = s.Name,
      })
      .ToArray();
    var route = DraftChanged || _route?.DispatchId != Load?.Id ? null : _route;
    var fingerprint = System.Text.Json.JsonSerializer.Serialize(points);
    var geometryChanged =
      _published != fingerprint || !ReferenceEquals(_publishedRoute, route);
    if (!geometryChanged && _publishedSelection == SelectedStopId)
      return;
    _published = fingerprint;
    _publishedRoute = route;
    _publishedSelection = SelectedStopId;
    try
    {
      _moduleTask ??= JS.InvokeAsync<IJSObjectReference>(
          "import",
          "./js/generated/dispatch/dispatch.js"
        )
        .AsTask();
      var module = await _moduleTask;
      if (
        _disposed
        || module is null
        || _published != fingerprint
        || !ReferenceEquals(_publishedRoute, route)
      )
        return;
      if (!geometryChanged)
      {
        await module.InvokeVoidAsync("selectStopMap", _element, SelectedStopId);
        return;
      }
      var ids = Stops.Select(x => x.Id).ToHashSet();
      var sections =
        route
          ?.Segments.Where(x =>
            ids.Contains(x.FromStopId) && ids.Contains(x.ToStopId)
          )
          .ToArray() ?? [];
      await module.InvokeVoidAsync(
        "showStopMap",
        _element,
        key,
        points,
        SelectedStopId,
        sections.Select(x => x.Points).ToArray(),
        sections.Select(x => x.Meaning).ToArray()
      );
    }
    catch (JSException)
    {
      if (!_disposed)
      {
        _error = "Map unavailable. Open Fleet Map to view the route.";
        StateHasChanged();
      }
    }
  }

  public async Task ActivateStopAsync(Guid stopId)
  {
    if (_disposed || _moduleTask is null)
      return;
    var module = await _moduleTask;
    if (_disposed)
      return;
    try
    {
      await module.InvokeVoidAsync("activateStopMap", _element, stopId);
    }
    catch (JSDisconnectedException) { }
  }

  public async ValueTask DisposeAsync()
  {
    if (_disposed)
      return;
    _disposed = true;
    _read?.Cancel();
    _read?.Dispose();
    if (_moduleTask is null)
      return;
    try
    {
      var module = await _moduleTask;
      if (module is null)
        return;
      await module.InvokeVoidAsync("disposeStopMap", _element);
      await module.DisposeAsync();
    }
    catch (JSException) { }
  }
}
