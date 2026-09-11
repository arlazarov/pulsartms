using Application.Features.Fuel.Models;

namespace Application.Features.Fuel.Interfaces;

public interface IFuelDiscountProvider
{
  Task<IReadOnlyList<FuelDiscountImportData>> GetDiscountsAsync(
    IReadOnlyCollection<string> importedMessageIds,
    CancellationToken cancellationToken = default
  );
}
