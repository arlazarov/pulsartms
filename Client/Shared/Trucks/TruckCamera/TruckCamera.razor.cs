using Client.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.JSInterop;

namespace Client.Shared.Trucks.TruckCamera;

public partial class TruckCamera
{
  [Inject] private ApiService Api { get; set; } = default!;
  [Inject] private IJSRuntime JS { get; set; } = default!;
  private ElementReference _dialog;
  private IJSObjectReference? _dialogModule;
  [Parameter] public Guid TruckId { get; set; }
  [Parameter] public string TruckNumber { get; set; } = "";
  private sealed record CameraImage(string Status, string? Url, DateTimeOffset? CapturedAt);
  private CameraImage? _image;
  private bool _open;
  private bool _refreshing;
  private string? _message;
  private CancellationTokenSource? _request;

  private Task OpenAsync() { _open = true; return RefreshAsync(); }
  protected override async Task OnAfterRenderAsync(bool firstRender)
  {
    if (!_open) return;
    _dialogModule ??= await JS.InvokeAsync<IJSObjectReference>("import", "./js/generated/shared/cameraDialog.js");
    if (_open) await _dialogModule.InvokeVoidAsync("show", _dialog);
  }
  private void OnKeyDown(KeyboardEventArgs e) { if (e.Key == "Escape") Close(); }
  private void ImageFailed() { _image = null; _message = "Image unavailable or expired. Request another snapshot."; }
  private void Close()
  {
    _open = false;
    _request?.Cancel();
    _request = null;
    _refreshing = false;
  }
  private async Task RefreshAsync()
  {
    if (_refreshing) return;
    _request?.Cancel();
    using var request = new CancellationTokenSource(TimeSpan.FromMinutes(5));
    _request = request;
    _refreshing = true;
    var previousCapture = _image?.CapturedAt;
    var earliestCapture = DateTimeOffset.UtcNow.AddSeconds(-15);
    bool IsFresh(CameraImage? image) => image is { Url: not null, CapturedAt: not null }
      && image.CapturedAt >= earliestCapture
      && (previousCapture is null || image.CapturedAt > previousCapture);
    _message = _image is null ? "Loading…" : null;
    try
    {
      var started = await Api.PostAsync<object, Guid>($"api/fleet/trucks/{TruckId}/camera", new { }, request.Token);
      request.Token.ThrowIfCancellationRequested();
      if (!started.Success) { _message = started.ErrorMessage; return; }
      await InvokeAsync(StateHasChanged);
      for (var attempt = 0; attempt < 60; attempt++)
      {
        if (attempt > 0) await Task.Delay(TimeSpan.FromSeconds(5), request.Token);
        var result = await Api.GetAsync<CameraImage>($"api/fleet/trucks/{TruckId}/camera/{started.Response}", request.Token);
        request.Token.ThrowIfCancellationRequested();
        if (!result.Success) { _message = result.ErrorMessage; return; }
        if (result.Response is { Url: not null, CapturedAt: not null } received
          && (_image?.CapturedAt is null || received.CapturedAt > _image.CapturedAt)) _image = received;
        if (IsFresh(_image))
        { _message = null; return; }
        var latest = await Api.GetAsync<CameraImage>($"api/fleet/trucks/{TruckId}/camera", request.Token);
        request.Token.ThrowIfCancellationRequested();
        if (latest.Success && IsFresh(latest.Response))
        { _image = latest.Response; _message = null; return; }
        if (result.Response?.Status is "failed" or "invalid" or "unavailable")
        { _message = "No snapshot available. The camera may be offline or not recording."; return; }
        _message = _image is null ? "Loading…" : null;
        await InvokeAsync(StateHasChanged);
      }
      _message = "Samsara did not deliver a new snapshot. Try Refresh.";
    }
    catch (OperationCanceledException) { if (_open && ReferenceEquals(_request, request)) _message = "Samsara did not deliver a new snapshot. Try Refresh."; }
    catch (Exception ex) when (ex is HttpRequestException or System.Text.Json.JsonException)
    { if (ReferenceEquals(_request, request)) _message = "Could not load the camera snapshot."; }
    finally
    {
      if (ReferenceEquals(_request, request))
      {
        _request = null;
        _refreshing = false;
        if (_open) await InvokeAsync(StateHasChanged);
      }
    }
  }
  public async ValueTask DisposeAsync()
  {
    _open = false;
    _request?.Cancel();
    if (_dialogModule is not null)
      try { await _dialogModule.DisposeAsync(); } catch (JSDisconnectedException) { }
  }
}
