using Application.Features.Fleet.Models;

namespace Application.Features.Fleet.Interfaces;

public interface IDriverHosProvider
{
  Task<IReadOnlyDictionary<string, DriverHosClocks>> GetClocksAsync(
    CancellationToken ct
  );
}
