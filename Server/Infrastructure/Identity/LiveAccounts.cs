using Application.Interfaces;
using Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Infrastructure.Identity;

// Whose sign-in, refresh and session still count: an active account of an
// existing, active carrier. Asked before any carrier is known, so it is not
// narrowed by one. A deactivated carrier's people could still sign in,
// refresh and work, although every background pass had already left the
// carrier out. An account whose carrier row is missing does not count
// either: nothing establishes that such a carrier may be served.
internal static class LiveAccounts
{
  public static IQueryable<User> Of(IAppDbContext db) =>
    db
      .Users.IgnoreQueryFilters()
      .Where(x =>
        x.IsActive && db.Companies.Any(c => c.Id == x.CompanyId && c.IsActive)
      );
}
