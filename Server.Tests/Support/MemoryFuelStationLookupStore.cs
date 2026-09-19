using System.Text.Json;
using Application.Features.Fuel.Interfaces;
using Application.Features.Fuel.Models;

namespace Server.Tests.Support;

internal sealed class MemoryFuelStationLookupStore : IFuelStationLookupStore
{
  private readonly Dictionary<string, string> states = new(
    StringComparer.OrdinalIgnoreCase
  );
  private readonly Dictionary<string, string> owners = new(
    StringComparer.OrdinalIgnoreCase
  );

  public Task<FuelStationLookupState?> ReadAsync(
    string stationId,
    CancellationToken ct
  ) =>
    Task.FromResult(
      states.TryGetValue(stationId, out var state)
        ? JsonSerializer.Deserialize<FuelStationLookupState>(state)
        : null
    );

  public async Task<bool> IsCurrentAsync(
    string stationId,
    string revision,
    CancellationToken ct
  )
  {
    var state = await ReadAsync(stationId, ct);
    return state is { Pending: false, ErrorCode: null }
      && state.Revision == revision;
  }

  public Task<bool> AcquireAsync(
    string stationId,
    string owner,
    DateTime now,
    CancellationToken ct
  )
  {
    if (owners.TryGetValue(stationId, out var current) && current != owner)
      return Task.FromResult(false);
    owners[stationId] = owner;
    return Task.FromResult(true);
  }

  public Task SaveAsync(
    string stationId,
    string owner,
    FuelStationLookupState state,
    CancellationToken ct
  )
  {
    if (!owners.TryGetValue(stationId, out var current) || current != owner)
      throw new InvalidOperationException("Lease lost.");
    states[stationId] = JsonSerializer.Serialize(state);
    return Task.CompletedTask;
  }

  public Task ReleaseAsync(string stationId, string owner, CancellationToken ct)
  {
    if (owners.TryGetValue(stationId, out var current) && current == owner)
      owners.Remove(stationId);
    return Task.CompletedTask;
  }
}
