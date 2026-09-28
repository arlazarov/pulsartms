using Application.Features.Dispatch.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace Infrastructure.Persistence;

public sealed class DispatchReadTicketStore(AppDbContext db)
  : IDispatchReadTickets
{
  public async Task<DispatchReadTicket> TakeAsync(
    string provider,
    CancellationToken ct
  )
  {
    if (db.Database.CurrentTransaction is not null)
      throw new InvalidOperationException(
        "A read ticket requires an independent commit."
      );
    var company = Company();
    var taken = await db
      .Database.SqlQuery<DispatchReadTicket>(
        $"""
        INSERT INTO "DispatchImportReads" AS reads
          ("CompanyId", "Provider", "LastTicket", "LastWrite")
        VALUES ({company}, {provider}, 1, 0)
        ON CONFLICT ("CompanyId", "Provider") DO UPDATE
          SET "LastTicket" = reads."LastTicket" + 1
        RETURNING "LastTicket" AS "Ticket", "LastWrite" AS "Written"
        """
      )
      .ToListAsync(ct);
    return taken.Single();
  }

  public async Task<long> WroteAsync(string provider, CancellationToken ct)
  {
    if (db.Database.CurrentTransaction is null)
      throw new InvalidOperationException(
        "A write is counted inside the pass that made it."
      );
    var company = Company();
    var counted = await db
      .Database.SqlQuery<long>(
        $"""
        UPDATE "DispatchImportReads"
          SET "LastWrite" = "LastWrite" + 1
        WHERE "CompanyId" = {company} AND "Provider" = {provider}
        RETURNING "LastWrite" AS "Value"
        """
      )
      .ToListAsync(ct);
    return counted.Single();
  }

  // A raw statement goes around the stamp, so it names the carrier itself.
  private Guid Company() =>
    db.ServingCompany
    ?? throw new InvalidOperationException(
      "A read ticket cannot be taken without a company."
    );
}
