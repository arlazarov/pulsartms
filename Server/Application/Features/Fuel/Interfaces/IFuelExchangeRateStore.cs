using Application.Features.Fuel.Models;

namespace Application.Features.Fuel.Interfaces;

public interface IFuelExchangeRateStore
{
  Task<bool> AcquireAsync(string owner, DateTime now, CancellationToken ct);
  Task<FuelExchangeRate?> ReadAsync(CancellationToken ct);

  // Commits the leased observation through the shared planning publication
  // scope. Acquisition, provider requests and release stay outside it.
  Task SaveAsync(string owner, FuelExchangeRate rate, CancellationToken ct);
  Task ReleaseAsync(string owner, CancellationToken ct);
}
