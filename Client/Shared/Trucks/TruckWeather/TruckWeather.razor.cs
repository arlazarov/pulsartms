using System.Net.Http.Json;
using System.Text.Json;
using Client.Models;
using Client.Models.DTO;
using Client.Models.DTO.Fleet;
using Client.Services;
using Microsoft.AspNetCore.Components;

namespace Client.Shared.Trucks.TruckWeather;

public partial class TruckWeather : IDisposable
{
  [Inject]
  private HttpClient Http { get; set; } = default!;

  [Inject]
  private TimeProvider Clock { get; set; } = default!;

  [CascadingParameter]
  public DisplayUnits Units { get; set; } = DisplayUnits.Default;

  [Parameter]
  public Guid TruckId { get; set; }

  [Parameter]
  public bool Current { get; set; }

  [Parameter]
  public PageVisibility? Visibility { get; set; }

  [Parameter]
  public CancellationToken OwnerCancellation { get; set; }

  private (Guid, bool)? selection;
  private CancellationTokenSource? lifetime;
  private WeatherReadingDto? reading;

  private string Value =>
    reading is { } value
      ? $"{Units.TemperatureValue(value.Celsius)} {Units.TemperatureUnit}"
      : "—";
  private string Title =>
    reading is { } value
      ? $"{value.Description} · Google Weather · "
        + $"{value.UpdatedAt.ToLocalTime():MMM d · hh:mm tt}"
      : "Weather unavailable";
  private string Icon =>
    reading?.Condition?.Trim().ToUpperInvariant() switch
    {
      { } type when type.Contains("THUNDER") => "thunder",
      { } type when type.Contains("SNOW") || type.Contains("SLEET") => "snow",
      { } type when type.Contains("RAIN") || type.Contains("STORM") => "rain",
      { } type when type.Contains("CLOUD") || type.Contains("FOG") => "cloud",
      "CLEAR" or "MOSTLY_CLEAR" => reading.IsDaytime ? "sun" : "moon",
      _ => "temperature",
    };

  protected override void OnParametersSet()
  {
    if (selection == (TruckId, Current))
      return;
    selection = (TruckId, Current);
    lifetime?.Cancel();
    lifetime?.Dispose();
    lifetime = null;
    reading = null;
    if (!Current || TruckId == Guid.Empty)
      return;
    lifetime = CancellationTokenSource.CreateLinkedTokenSource(
      OwnerCancellation
    );
    _ = RefreshAsync(TruckId, lifetime.Token);
  }

  private async Task RefreshAsync(Guid id, CancellationToken ct)
  {
    try
    {
      var emptyReads = 0;
      var nextRead = Clock.GetUtcNow();
      while (true)
      {
        await WaitForReadAsync(nextRead, ct);
        WeatherReadingDto? next = null;
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(12));
        try
        {
          var result = await Http.GetFromJsonAsync<
            RequestResponseDTO<WeatherReadingDto>
          >($"api/fleet/trucks/{id}/weather", timeout.Token);
          if (result?.Success == true)
            next = result.Response;
        }
        catch (Exception ex)
          when (ex
              is HttpRequestException
                or JsonException
                or OperationCanceledException
          )
        {
          ct.ThrowIfCancellationRequested();
        }
        ct.ThrowIfCancellationRequested();
        if (
          next is not null
          && (
            next.UpdatedAt < Clock.GetUtcNow().AddHours(-1)
            || next.UpdatedAt > Clock.GetUtcNow().AddMinutes(5)
          )
        )
          next = null;
        reading = next;
        await InvokeAsync(StateHasChanged);
        emptyReads = next is null ? emptyReads + 1 : 0;
        var delay =
          next is not null ? TimeSpan.FromMinutes(10)
          : emptyReads <= 5 ? TimeSpan.FromSeconds(15)
          : TimeSpan.FromMinutes(1);
        nextRead = Clock.GetUtcNow() + delay;
      }
    }
    catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
  }

  private async Task WaitForReadAsync(
    DateTimeOffset nextRead,
    CancellationToken ct
  )
  {
    while (true)
    {
      ct.ThrowIfCancellationRequested();
      var changed = Visibility?.Changed ?? CancellationToken.None;
      var visible = Visibility?.IsVisible != false;
      var remaining = nextRead - Clock.GetUtcNow();
      if (visible && remaining <= TimeSpan.Zero)
        return;
      using var waiting = CancellationTokenSource.CreateLinkedTokenSource(
        ct,
        changed
      );
      try
      {
        await Task.Delay(
          visible ? remaining : Timeout.InfiniteTimeSpan,
          Clock,
          waiting.Token
        );
      }
      catch (OperationCanceledException) when (!ct.IsCancellationRequested) { }
    }
  }

  public void Dispose()
  {
    lifetime?.Cancel();
    lifetime?.Dispose();
  }
}
