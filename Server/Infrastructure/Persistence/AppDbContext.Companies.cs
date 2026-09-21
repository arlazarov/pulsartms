using System.Reflection;
using Application.Interfaces;
using Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace Infrastructure.Persistence;

// Runtime contexts require a company for writes. Design-time and explicit
// tooling contexts without the identity service retain bootstrap access.
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
    var company = ServingCompany;
    foreach (var entry in ChangeTracker.Entries<ICompanyOwned>())
    {
      if (
        entry.State
        is not (
          EntityState.Added
          or EntityState.Modified
          or EntityState.Deleted
        )
      )
        continue;
      if (companies is not null && company is null)
        throw new InvalidOperationException(
          "Company-owned writes require a selected company."
        );
      if (entry.State == EntityState.Added && entry.Entity.CompanyId == default)
        entry.Entity.CompanyId = company!.Value;
      // Design-time and fixture contexts have no company service. Runtime
      // contexts must neither write a foreign row nor transfer its ownership.
      if (
        companies is not null
        && (
          entry.Entity.CompanyId != company
          || entry.State != EntityState.Added
            && entry.Property(x => x.CompanyId).OriginalValue != company
        )
      )
        throw new InvalidOperationException(
          "Company-owned writes cannot cross company boundaries."
        );
    }
  }
}
