using Application.Features.Execution.Interfaces;
using Domain.Entities.Execution;
using Microsoft.EntityFrameworkCore;

namespace Infrastructure.Persistence;

public sealed class ExecutionPlanningStore(AppDbContext db)
  : IExecutionPlanningStore
{
  public async Task<ExecutionPlanningChange?> ClaimAsync(
    DateTime now,
    CancellationToken ct
  )
  {
    var lease = Guid.NewGuid();
    var until = now.AddMinutes(10);
    if (db.Database.IsNpgsql())
    {
      var claimed = await db
        .ExecutionPlanningChanges.FromSqlInterpolated(
          $"""
          WITH candidate AS (
            SELECT "Id" FROM "ExecutionPlanningChanges"
            WHERE "CompletedAt" IS NULL AND "AvailableAt" <= {now}
              AND ("LeaseUntil" IS NULL OR "LeaseUntil" <= {now})
            ORDER BY "AvailableAt", "Id"
            LIMIT 1 FOR UPDATE SKIP LOCKED
          )
          UPDATE "ExecutionPlanningChanges" AS work
          SET "LeaseId" = {lease}, "LeaseUntil" = {until},
            "Attempts" = work."Attempts" + 1
          FROM candidate WHERE work."Id" = candidate."Id"
          RETURNING work.*
          """
        )
        .AsNoTracking()
        .ToListAsync(ct);
      return claimed.SingleOrDefault();
    }
    var work = await db
      .ExecutionPlanningChanges.AsNoTracking()
      .Where(x =>
        x.CompletedAt == null
        && x.AvailableAt <= now
        && (x.LeaseUntil == null || x.LeaseUntil <= now)
      )
      .OrderBy(x => x.AvailableAt)
      .ThenBy(x => x.Id)
      .FirstOrDefaultAsync(ct);
    if (work is null)
      return null;
    var updated = await db
      .ExecutionPlanningChanges.Where(x =>
        x.Id == work.Id
        && x.CompletedAt == null
        && x.AvailableAt <= now
        && (x.LeaseUntil == null || x.LeaseUntil <= now)
      )
      .ExecuteUpdateAsync(
        setters =>
          setters
            .SetProperty(x => x.LeaseId, lease)
            .SetProperty(x => x.LeaseUntil, until)
            .SetProperty(x => x.Attempts, x => x.Attempts + 1),
        ct
      );
    if (updated != 1)
      return null;
    work.LeaseId = lease;
    work.LeaseUntil = until;
    work.Attempts++;
    return work;
  }

  public async Task<bool> CompleteAsync(
    ExecutionPlanningChange work,
    DateTime now,
    bool succeeded,
    CancellationToken ct
  )
  {
    if (work.LeaseId is not { } lease)
      return false;
    var available = now.AddSeconds(
      Math.Min(900, 15 * Math.Pow(2, Math.Min(work.Attempts, 6)))
    );
    var updated = await db
      .ExecutionPlanningChanges.Where(x =>
        x.Id == work.Id
        && x.LeaseId == lease
        && x.LeaseUntil > now
        && x.CompletedAt == null
      )
      .ExecuteUpdateAsync(
        setters =>
          setters
            .SetProperty(x => x.CompletedAt, succeeded ? now : (DateTime?)null)
            .SetProperty(x => x.AvailableAt, available)
            .SetProperty(x => x.LeaseId, (Guid?)null)
            .SetProperty(x => x.LeaseUntil, (DateTime?)null),
        ct
      );
    return updated == 1;
  }

  public Task PruneAsync(DateTime before, CancellationToken ct) =>
    db
      .ExecutionPlanningChanges.Where(x => x.CompletedAt < before)
      .ExecuteDeleteAsync(ct);
}
