using Application.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace Infrastructure.Persistence;

public sealed class CompanyRoster(AppDbContext db) : ICompanyRoster
{
  // Companies is not a carrier's table - it is the list of them - so this
  // read is not filtered and does not need to be.
  public async Task<IReadOnlyList<Guid>> ActiveAsync(CancellationToken ct) =>
    await db
      .Companies.AsNoTracking()
      .Where(company => company.IsActive)
      .OrderBy(company => company.Key)
      .Select(company => company.Id)
      .ToListAsync(ct);
}
