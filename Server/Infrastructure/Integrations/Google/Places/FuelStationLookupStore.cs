using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Application.Features.Fuel.Interfaces;
using Application.Features.Fuel.Models;
using Infrastructure.Persistence;
using Infrastructure.Synchronization;
using Microsoft.Extensions.DependencyInjection;

namespace Infrastructure.Integrations.Google.Places;

public sealed class FuelStationLookupStore(AppDbContext importDb, IServiceScopeFactory scopes) : IFuelStationLookupStore
{
  private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

  public async Task<bool> IsCurrentAsync(string stationId, string revision, CancellationToken ct)
  {
    // The import already owns its transaction/connection; validation must not acquire another.
    var json = await new CheckpointLeaseStore(importDb, Id(stationId)).ReadAsync(ct);
    var state = json is null ? null : JsonSerializer.Deserialize<FuelStationLookupState>(json, Json);
    return !string.IsNullOrEmpty(revision) && state is { Pending: false, ErrorCode: null } && state.Revision == revision;
  }

  public async Task<FuelStationLookupState?> ReadAsync(string stationId, CancellationToken ct)
  {
    await using var scope = scopes.CreateAsyncScope();
    var json = await Checkpoint(scope.ServiceProvider, stationId).ReadAsync(ct);
    return json is null ? null : JsonSerializer.Deserialize<FuelStationLookupState>(json, Json);
  }

  public async Task<bool> AcquireAsync(string stationId, string owner, DateTime now, CancellationToken ct)
  {
    await using var scope = scopes.CreateAsyncScope();
    return await Checkpoint(scope.ServiceProvider, stationId).AcquireAsync(owner, now, ct);
  }

  public async Task SaveAsync(string stationId, string owner, FuelStationLookupState state, CancellationToken ct)
  {
    // A separate context commits retry state outside the importing message's transaction.
    await using var scope = scopes.CreateAsyncScope();
    await Checkpoint(scope.ServiceProvider, stationId).SaveAsync(owner, JsonSerializer.Serialize(state, Json), ct);
  }

  public async Task ReleaseAsync(string stationId, string owner, CancellationToken ct)
  {
    await using var scope = scopes.CreateAsyncScope();
    await Checkpoint(scope.ServiceProvider, stationId).ReleaseAsync(owner, ct);
  }

  private static CheckpointLeaseStore Checkpoint(IServiceProvider services, string stationId) =>
    new(services.GetRequiredService<AppDbContext>(), Id(stationId));

  private static Guid Id(string stationId)
  {
    var hash = SHA256.HashData(Encoding.UTF8.GetBytes("fuel-station-lookup:" + stationId.Trim().ToUpperInvariant()));
    return new(hash.AsSpan(0, 16));
  }
}
