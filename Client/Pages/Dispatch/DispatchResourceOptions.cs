using Client.Models.DTO;
using Client.Models.DTO.Mileage;
using Client.Services;

namespace Client.Pages.Dispatch;

public sealed record DispatchResourceOptionsResult(
  RequestResponseDTO<MileageFleetList<MileageUnitOption>> Trucks,
  RequestResponseDTO<MileageFleetList<MileageUnitOption>> Trailers,
  RequestResponseDTO<MileageFleetList<MileageDriverOption>> Drivers
)
{
  public bool Success => Trucks.Success && Trailers.Success && Drivers.Success;
}

public sealed class DispatchResourceOptions
{
  private Task<DispatchResourceOptionsResult>? _read;

  public Task<DispatchResourceOptionsResult> ReadAsync(
    ApiService api,
    CancellationToken ct
  )
  {
    if (
      _read is { IsCompletedSuccessfully: true } && !_read.Result.Success
      || _read is { IsFaulted: true } or { IsCanceled: true }
    )
      _read = null;
    return _read ??= LoadAsync(api, ct);
  }

  private static async Task<DispatchResourceOptionsResult> LoadAsync(
    ApiService api,
    CancellationToken ct
  )
  {
    var trucks = api.GetAsync<MileageFleetList<MileageUnitOption>>(
      "api/fleet/trucks",
      ct
    );
    var trailers = api.GetAsync<MileageFleetList<MileageUnitOption>>(
      "api/fleet/trailers",
      ct
    );
    var drivers = api.GetAsync<MileageFleetList<MileageDriverOption>>(
      "api/fleet/drivers",
      ct
    );
    await Task.WhenAll(trucks, trailers, drivers);
    return new(trucks.Result, trailers.Result, drivers.Result);
  }
}
