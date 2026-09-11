using Client.Models.DTO;
using Client.Models.DTO.Fleet;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.JSInterop;

namespace Client.Pages.FleetMap;

internal sealed class FleetStationLayer(HttpClient http, IJSObjectReference map) : IDisposable
{
  private CancellationTokenSource? _request;
  private bool _disposed;
  public DateOnly? LoadedDate { get; private set; }
  public string? Error { get; private set; }

  public void Cancel()
  {
    _request?.Cancel();
    _request = null;
    Error = null;
  }

  public async Task LoadAsync(DateOnly date, Func<bool> useIfta, CancellationToken cancellationToken)
  {
    if (_disposed) return;
    Cancel();
    using var request = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
    _request = request;
    var dateText = date.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
    try
    {
      var result = await http.GetFromJsonAsync<ApiResponse<List<FuelStationMapDto>>>(
        $"api/fuel/stations?date={dateText}", request.Token);
      if (!IsCurrent(request)) return;
      if (result?.Success != true || result.Response is null)
        throw new HttpRequestException("Fuel stations are unavailable.");
      await map.InvokeVoidAsync("setStations", result.Response, dateText, useIfta());
      if (!IsCurrent(request)) return;
      LoadedDate = date;
      Error = null;
    }
    catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException or JsonException or JSException)
    {
      if (IsCurrent(request))
        Error = "Fuel stations could not be loaded. Showing the last available data; retry to update the selected date.";
    }
    finally { if (ReferenceEquals(_request, request)) _request = null; }
  }

  private bool IsCurrent(CancellationTokenSource request) =>
    !_disposed && !request.IsCancellationRequested && ReferenceEquals(_request, request);

  public void Dispose()
  {
    if (_disposed) return;
    _disposed = true;
    Cancel();
  }
}
