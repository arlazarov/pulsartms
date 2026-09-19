using Application.Features.Routing.Services.FuelPlanning;

namespace Application.Features.Routing.Interfaces;

public interface IFuelWorkInputsReader
{
  Task<FuelWorkInputs> ReadFreshAsync(Guid truckId, CancellationToken ct);
  Task<FuelWorkInputs?> ReadDisplayAsync(Guid truckId, CancellationToken ct);
}
