using Application.Features.Fuel.Interfaces;
using Application.Features.Fuel.Models;

namespace Server.Tests.Support;

public sealed class StubFuelExchangeRateProvider : IFuelExchangeRateProvider
{
  public int Calls { get; private set; }
  public Func<CancellationToken, Task<FuelExchangeRate>> Read { get; set; } =
    _ =>
      throw new InvalidOperationException("No exchange-rate request expected.");

  public Task<FuelExchangeRate> ReadAsync(CancellationToken ct)
  {
    Calls++;
    return Read(ct);
  }
}
