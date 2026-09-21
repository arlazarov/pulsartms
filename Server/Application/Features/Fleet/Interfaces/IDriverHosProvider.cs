using Domain.Models.Fleet;

namespace Application.Features.Fleet.Interfaces;

public interface IDriverHosProvider
{
  Task<IReadOnlyDictionary<string, DriverHosClocks>> GetClocksAsync(
    CancellationToken ct
  );
}
