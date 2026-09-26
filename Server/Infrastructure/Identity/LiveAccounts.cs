using Application.Interfaces;
using Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Infrastructure.Identity;

// Whose sign-in, refresh and session still count: an active account of a
// carrier that has not been deactivated. Asked before any carrier is known,
// so it is not narrowed by one. A deactivated carrier's people could still
// sign in, refresh and work, although every background pass had already
// left the carrier out. A missing company row is not a deactivated one: a
// test or design-time host has none.
internal static class LiveAccounts
{
  public static IQueryable<User> Of(IAppDbContext db) =>
    db
      .Users.IgnoreQueryFilters()
      .Where(x =>
        x.IsActive && !db.Companies.Any(c => c.Id == x.CompanyId && !c.IsActive)
      );
}
