using System.Reflection;
using Application.Interfaces;
using Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace Infrastructure.Persistence;

// Keeping one carrier's rows away from another's.
//
// Two rules, applied to every table that carries ICompanyOwned, neither of
// which any query or command has to remember:
//
//   reading  - every query is narrowed to the carrier being served, by a
//              filter EF puts into the SQL itself. A query cannot forget
//              the filter, because there is nowhere to write it.
//   writing  - every new row is stamped with that carrier on its way to
//              the database, so a command cannot save into the wrong one
//              by leaving a field unset.
//
// Who is being served depends on how the context was built, and the two
// cases are different on purpose:
//
//   a host that knows about carriers - a server - and nobody has been
//   identified yet: the filter matches nothing. Background work that
//   forgot to say whose pass it is reads an empty database rather than
//   everybody's, which is the safe way to be wrong.
//
//   a host that does not - a migration, a design-time tool, a test: there
//   is no request and no tenancy, so it serves the one carrier this
//   system was built for. A server never takes this path, because the
//   server registers ICurrentCompany.
public partial class AppDbContext
{
  private bool asked;
  private ICurrentCompany? companies;
  private Guid? standing;

  // Read by the query filters. A property rather than a captured value
  // because EF reads it again for every query: one context serves one
  // request, but a background pass changes carrier between rounds on the
  // same context.
  public Guid? ServingCompany
  {
    get
    {
      if (!asked)
      {
        asked = true;
        var services = this.GetService<IDbContextOptions>()
          .FindExtension<CoreOptionsExtension>()
          ?.ApplicationServiceProvider;
        companies = services?.GetService<ICurrentCompany>();
        // No ICurrentCompany anywhere is not "nobody has signed in" - it
        // is a host with no notion of carriers at all: a migration, a
        // design-time tool, a test. That is a single-carrier world and it
        // serves the one carrier. A server always registers the service,
        // so on a server a null answer means nobody has been identified
        // yet, and then nothing is visible.
        if (companies is null)
          standing = Company.Amf;
      }
      return companies?.Id ?? standing;
    }
  }

  private static readonly MethodInfo FilterOne = typeof(AppDbContext).GetMethod(
    nameof(FilterByCompany),
    BindingFlags.NonPublic | BindingFlags.Instance
  )!;

  private void FilterEveryCompanyOwnedTable(ModelBuilder modelBuilder)
  {
    foreach (
      var entity in modelBuilder
        .Model.GetEntityTypes()
        .Where(x => typeof(ICompanyOwned).IsAssignableFrom(x.ClrType))
        .ToArray()
    )
    {
      FilterOne.MakeGenericMethod(entity.ClrType).Invoke(this, [modelBuilder]);
      modelBuilder
        .Entity(entity.ClrType)
        .HasIndex(nameof(ICompanyOwned.CompanyId));
    }
  }

  private void FilterByCompany<T>(ModelBuilder modelBuilder)
    where T : class, ICompanyOwned =>
    modelBuilder
      .Entity<T>()
      .HasQueryFilter(row => row.CompanyId == ServingCompany);

  private void StampNewRowsWithTheCompany()
  {
    if (ServingCompany is not { } company)
      return;
    foreach (var entry in ChangeTracker.Entries<ICompanyOwned>())
      if (entry.State == EntityState.Added && entry.Entity.CompanyId == default)
        entry.Entity.CompanyId = company;
  }
}
