using Application.Features.Fleet.Models;

namespace Application.Features.Fleet.Interfaces;

public interface IDriverHosRefreshProvider
{
  Task<IReadOnlyDictionary<string, DriverHosClocks>> RefreshClocksAsync(
    CancellationToken ct
  );
}
