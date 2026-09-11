using Application.Features.Fuel.Models;

namespace Application.Features.Fuel.Interfaces;

public interface IFuelStationLookupStore
{
  Task<FuelStationLookupState?> ReadAsync(string stationId, CancellationToken ct);
  Task<bool> IsCurrentAsync(string stationId, string revision, CancellationToken ct);
  Task<bool> AcquireAsync(string stationId, string owner, DateTime now, CancellationToken ct);
  Task SaveAsync(string stationId, string owner, FuelStationLookupState state, CancellationToken ct);
  Task ReleaseAsync(string stationId, string owner, CancellationToken ct);
}
