using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json;
using Client.Models.DTO;
using Client.Models.DTO.Fleet;
using Microsoft.JSInterop;

namespace Client.Pages.FleetMap;

internal sealed class FleetStationLayer(HttpClient http, IJSObjectReference map)
  : IDisposable
{
  private CancellationTokenSource? _request;
  private CancellationTokenSource? _priceRequest;
  private DateOnly? _pricesDate;
  private List<FuelMapPriceDto>? _prices;
  private bool _disposed;
  public DateOnly? LoadedDate { get; private set; }
  public string? Error { get; private set; }
  public string? PriceError { get; private set; }

  public async Task LoadPricesAsync(
    DateOnly date,
    Func<bool> useIfta,
    CancellationToken cancellationToken
  )
  {
    if (_disposed)
      return;
    _priceRequest?.Cancel();
    using var request = CancellationTokenSource.CreateLinkedTokenSource(
      cancellationToken
    );
    _priceRequest = request;
    var dateText = date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
    try
    {
      if (_pricesDate == date && _prices is not null)
      {
        await map.InvokeVoidAsync(
          "setPriceOverview",
          request.Token,
          _prices,
          dateText,
          useIfta()
        );
        if (IsPriceCurrent(request))
          PriceError = null;
        return;
      }
      await map.InvokeVoidAsync(
        "setPriceOverview",
        request.Token,
        null,
        dateText,
        useIfta()
      );
      if (!IsPriceCurrent(request))
        return;
      var result = await http.GetFromJsonAsync<
        ApiResponse<List<FuelMapPriceDto>>
      >($"api/fuel/price-overview?date={dateText}", request.Token);
      if (!IsPriceCurrent(request))
        return;
      if (result?.Success != true || result.Response is null)
        throw new HttpRequestException("Fuel marker prices are unavailable.");
      await map.InvokeVoidAsync(
        "setPriceOverview",
        request.Token,
        result.Response,
        dateText,
        useIfta()
      );
      if (!IsPriceCurrent(request))
        return;
      _pricesDate = date;
      _prices = result.Response;
      PriceError = null;
    }
    catch (Exception ex)
      when (ex
          is HttpRequestException
            or OperationCanceledException
            or JsonException
            or JSException
      )
    {
      if (IsPriceCurrent(request))
        PriceError =
          "Fuel marker prices could not be loaded. Retry to show price colors.";
    }
    finally
    {
      if (ReferenceEquals(_priceRequest, request))
        _priceRequest = null;
    }
  }

  private bool IsPriceCurrent(CancellationTokenSource request) =>
    !_disposed
    && !request.IsCancellationRequested
    && ReferenceEquals(_priceRequest, request);

  public void Cancel()
  {
    _request?.Cancel();
    _request = null;
    Error = null;
  }

  public async Task LoadAsync(
    DateOnly date,
    Func<bool> useIfta,
    CancellationToken cancellationToken
  )
  {
    if (_disposed)
      return;
    Cancel();
    using var request = CancellationTokenSource.CreateLinkedTokenSource(
      cancellationToken
    );
    _request = request;
    var dateText = date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
    try
    {
      var result = await http.GetFromJsonAsync<
        ApiResponse<List<FuelStationMapDto>>
      >($"api/fuel/stations?date={dateText}", request.Token);
      if (!IsCurrent(request))
        return;
      if (result?.Success != true || result.Response is null)
        throw new HttpRequestException("Fuel stations are unavailable.");
      await map.InvokeVoidAsync(
        "setStations",
        result.Response,
        dateText,
        useIfta()
      );
      if (!IsCurrent(request))
        return;
      LoadedDate = date;
      Error = null;
    }
    catch (Exception ex)
      when (ex
          is HttpRequestException
            or OperationCanceledException
            or JsonException
            or JSException
      )
    {
      if (IsCurrent(request))
        Error =
          "Fuel stations could not be loaded. Showing the last available data; retry to update the selected date.";
    }
    finally
    {
      if (ReferenceEquals(_request, request))
        _request = null;
    }
  }

  private bool IsCurrent(CancellationTokenSource request) =>
    !_disposed
    && !request.IsCancellationRequested
    && ReferenceEquals(_request, request);

  public void Dispose()
  {
    if (_disposed)
      return;
    _disposed = true;
    Cancel();
    _priceRequest?.Cancel();
    _priceRequest = null;
    _prices = null;
  }
}
