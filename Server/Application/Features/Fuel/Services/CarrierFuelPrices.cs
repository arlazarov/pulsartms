using Application.Features.Fuel.Interfaces;
using Application.Features.Fuel.Queries.GetFuelStations;

namespace Application.Features.Fuel.Services;

public sealed class CarrierFuelPrices(ISender sender) : ICarrierFuelPrices
{
  public async Task<List<FuelStationDto>?> ReadAsync(
    DateOnly date,
    CancellationToken ct
  )
  {
    var response = await sender.Send(new GetFuelStationsQuery(date), ct);
    return response.Success ? response.Response : null;
  }
}
