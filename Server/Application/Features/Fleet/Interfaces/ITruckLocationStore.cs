using Domain.Models.Fleet;

namespace Application.Features.Fleet.Interfaces;

// Where the latest truck positions outlive one process.
public interface ITruckLocationStore
{
  // Positions observed recently enough to be worth drawing.
  Task<IReadOnlyList<TruckLocation>> ReadAsync(CancellationToken ct);

  Task WriteAsync(IReadOnlyList<TruckLocation> trucks, CancellationToken ct);
}
