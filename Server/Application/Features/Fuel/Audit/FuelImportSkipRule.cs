using Application.Diagnostics.Consistency;
using Application.Features.Fuel.Commands.ImportFuelDiscounts;

namespace Application.Features.Fuel.Audit;

// A discount message the import could not read (audit F20). Its prices
// are missing; while it is still in the mailbox window a corrected parser
// imports it and the record goes. Older ones stay recorded but are no
// longer findings: nothing retries them. Read only.
public sealed class FuelImportSkipRule(IAppDbContext db) : IConsistencyRule
{
  public ConsistencyRuleInfo Info { get; } =
    new(
      "fuel.import-message-skipped",
      1,
      "Fuel: ImportFuelDiscountsHandler",
      ConsistencyCondition.Review,
      ConsistencySeverity.Warning,
      "Every discount message in the mailbox window is imported.",
      "Open the message named in the log; fix what made it unreadable "
        + "while it is in the window, or enter its prices by hand."
    );

  public async Task<ConsistencyPage> ReadAsync(
    ConsistencyPageRequest request,
    CancellationToken ct
  )
  {
    Guid? after = request.After is null ? null : Guid.Parse(request.After);
    var window = request.Now - ImportFuelDiscountsHandler.Furthest;
    var rows = await db
      .FuelImportSkips.AsNoTracking()
      .Where(x =>
        x.CompanyId == request.Company
        && x.SkippedAt >= window
        && (after == null || x.Id.CompareTo(after.Value) > 0)
      )
      .OrderBy(x => x.Id)
      .Select(x => new
      {
        x.Id,
        x.Reason,
        x.SkippedAt,
      })
      .Take(request.Limit + 1)
      .ToListAsync(ct);
    return new(
      [
        .. rows.Take(request.Limit)
          .Select(x => new ConsistencyObservation(
            x.Id.ToString(),
            $"reason:{x.Reason}",
            new Dictionary<string, string>
            {
              ["reason"] = x.Reason,
              ["skippedAt"] = x.SkippedAt.ToString("O"),
            }
          )),
      ],
      rows.Count > request.Limit
    );
  }
}
