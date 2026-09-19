using Application.Features.Fuel.Queries.GetFuelStations;

namespace Application.Features.Fuel.Interfaces;

// The prices a carrier pays on a pricing day, for consumers outside Fuel.
// Fuel owns what a price is: the programme price where a programme applies,
// the known retail price otherwise, and no price at all when neither is
// known. A station without a price is still returned, because reaching it
// and comparing its cost are separate questions.
public interface ICarrierFuelPrices
{
  // Null means the prices could not be read. An empty list means there are
  // none for that day; the two are not the same.
  Task<List<FuelStationDto>?> ReadAsync(DateOnly date, CancellationToken ct);
}
