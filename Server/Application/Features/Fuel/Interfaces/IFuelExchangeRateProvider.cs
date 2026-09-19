using Application.Features.Fuel.Models;

namespace Application.Features.Fuel.Interfaces;

public interface IFuelExchangeRateProvider
{
  Task<FuelExchangeRate> ReadAsync(CancellationToken ct);
}
