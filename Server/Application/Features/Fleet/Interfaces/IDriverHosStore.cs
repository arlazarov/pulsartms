using Domain.Models.Fleet;

namespace Application.Features.Fleet.Interfaces;

// Where the latest hours readings outlive one process.
public interface IDriverHosStore
{
  Task<IReadOnlyDictionary<string, DriverHosClocks>> ReadAsync(
    CancellationToken ct
  );

  // Replaces the stored readings with what the provider last returned.
  Task WriteAsync(
    IReadOnlyDictionary<string, DriverHosClocks> clocks,
    CancellationToken ct
  );
}
