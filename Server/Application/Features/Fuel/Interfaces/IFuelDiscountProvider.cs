using Application.Features.Fuel.Models;

namespace Application.Features.Fuel.Interfaces;

public interface IFuelDiscountProvider
{
  // Messages received since the given time, other than those imported.
  Task<IReadOnlyList<FuelDiscountImportData>> GetDiscountsAsync(
    IReadOnlyCollection<string> importedMessageIds,
    DateTime since,
    CancellationToken cancellationToken = default
  );
}
