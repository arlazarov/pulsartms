using Domain.Models.Fleet;

namespace Application.Features.Fleet.Interfaces;

public interface IDriverHosProvider
{
  Task<IReadOnlyDictionary<string, DriverHosClocks>> GetClocksAsync(
    CancellationToken ct
  );

  // Drivers whose duty status changed between two readings, or who were
  // read for the first time. Clocks counting down between readings are not
  // a change. A provider that cannot tell never raises it.
  event Action<IReadOnlyCollection<string>>? DutyChanged
  {
    add { }
    remove { }
  }
}
