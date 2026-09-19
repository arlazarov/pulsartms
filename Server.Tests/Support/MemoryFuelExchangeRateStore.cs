using Application.Features.Fuel.Interfaces;
using Application.Features.Fuel.Models;

namespace Server.Tests.Support;

public sealed class MemoryFuelExchangeRateStore : IFuelExchangeRateStore
{
  private readonly object gate = new();
  private string? lease;
  public FuelExchangeRate? Rate { get; set; }
  public int Reads { get; private set; }
  public int Saves { get; private set; }
  public int Releases { get; private set; }
  public Action? OnAcquire { get; set; }

  public Task<bool> AcquireAsync(
    string owner,
    DateTime now,
    CancellationToken ct
  )
  {
    ct.ThrowIfCancellationRequested();
    lock (gate)
    {
      if (lease is not null)
        return Task.FromResult(false);
      lease = owner;
      OnAcquire?.Invoke();
      return Task.FromResult(true);
    }
  }

  public Task<FuelExchangeRate?> ReadAsync(CancellationToken ct)
  {
    ct.ThrowIfCancellationRequested();
    lock (gate)
    {
      Reads++;
      return Task.FromResult(Rate);
    }
  }

  public Task SaveAsync(
    string owner,
    FuelExchangeRate rate,
    CancellationToken ct
  )
  {
    ct.ThrowIfCancellationRequested();
    lock (gate)
    {
      if (lease != owner)
        throw new InvalidOperationException("The fixture lease was lost.");
      Rate = rate;
      Saves++;
    }
    return Task.CompletedTask;
  }

  public Task ReleaseAsync(string owner, CancellationToken ct)
  {
    lock (gate)
    {
      if (lease == owner)
        lease = null;
      Releases++;
    }
    return Task.CompletedTask;
  }
}
