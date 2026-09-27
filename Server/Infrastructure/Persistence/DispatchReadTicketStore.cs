using Application.Features.Dispatch.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace Infrastructure.Persistence;

public sealed class DispatchReadTicketStore(AppDbContext db)
  : IDispatchReadTickets
{
  public async Task<long> TakeAsync(string provider, CancellationToken ct)
  {
    if (db.Database.CurrentTransaction is not null)
      throw new InvalidOperationException(
        "A read ticket requires an independent commit."
      );
    // A raw statement goes around the stamp, so it names the carrier itself.
    var company =
      db.ServingCompany
      ?? throw new InvalidOperationException(
        "A read ticket cannot be taken without a company."
      );
    var taken = await db
      .Database.SqlQuery<long>(
        $"""
        INSERT INTO "DispatchImportReads" AS reads
          ("CompanyId", "Provider", "LastTicket")
        VALUES ({company}, {provider}, 1)
        ON CONFLICT ("CompanyId", "Provider") DO UPDATE
          SET "LastTicket" = reads."LastTicket" + 1
        RETURNING "LastTicket" AS "Value"
        """
      )
      .ToListAsync(ct);
    return taken.Single();
  }
}
