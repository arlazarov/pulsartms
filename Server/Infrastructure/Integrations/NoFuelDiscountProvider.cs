using Application.Features.Fuel.Interfaces;
using Application.Features.Fuel.Models;

namespace Infrastructure.Integrations;

// FuelDiscounts:Source "none": customers without a fuel card import nothing.
public sealed class NoFuelDiscountProvider : IFuelDiscountProvider
{
  public Task<IReadOnlyList<FuelDiscountImportData>> GetDiscountsAsync(
    IReadOnlyCollection<string> importedMessageIds, CancellationToken cancellationToken = default) =>
    Task.FromResult<IReadOnlyList<FuelDiscountImportData>>([]);
}
