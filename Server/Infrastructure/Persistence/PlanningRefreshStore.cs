using Application.Features.Routing.Interfaces;
using Domain.Entities.Dispatch;
using Domain.Models.Routing;
using Microsoft.EntityFrameworkCore;

namespace Infrastructure.Persistence;

public sealed class PlanningRefreshStore(AppDbContext db)
  : IPlanningRefreshStore
{
  public async Task<PlanningRefreshState> RequestAsync(
    PlanningScope scope,
    string inputSignature,
    DateTime now,
    CancellationToken ct
  )
  {
    if (db.Database.CurrentTransaction is not null)
      throw new InvalidOperationException(
        "On-demand planning requests require an independent commit."
      );
    var id = FormattableString.Invariant(
      $"{scope.DispatchId:N}:{scope.ExecutionLegId:N}:{scope.AssignmentRevision}"
    );
    await db.Database.ExecuteSqlInterpolatedAsync(
      $"""
      INSERT INTO "PlanningRefreshRequests" AS request (
        "Id", "DispatchId", "ExecutionLegId", "AssignmentRevision",
        "InputSignature", "RequestedVersion", "CompletedVersion",
        "RequestedAt", "AvailableAt", "Attempts"
      ) VALUES (
        {id}, {scope.DispatchId}, {scope.ExecutionLegId},
        {scope.AssignmentRevision}, {inputSignature}, 1, 0, {now}, {now}, 0
      ) ON CONFLICT ("Id") DO UPDATE SET
        "InputSignature" = EXCLUDED."InputSignature",
        "RequestedVersion" = request."RequestedVersion" + 1,
        "RequestedAt" = EXCLUDED."RequestedAt",
        "AvailableAt" = EXCLUDED."AvailableAt",
        "Attempts" = 0
      WHERE request."InputSignature" <> EXCLUDED."InputSignature"
        OR (request."CompletedVersion" = request."RequestedVersion"
          AND request."AvailableAt" <= {now})
      """,
      ct
    );
    return await db
      .PlanningRefreshRequests.Where(x => x.Id == id)
      .Select(x => new PlanningRefreshState(
        x.CompletedVersion < x.RequestedVersion,
        x.AvailableAt
      ))
      .SingleAsync(ct);
  }

  public async Task<PlanningRefreshWork?> ClaimAsync(
    DateTime now,
    TimeSpan leaseDuration,
    CancellationToken ct
  )
  {
    var lease = Guid.NewGuid();
    var until = now.Add(leaseDuration);
    PlanningRefreshRequest? work;
    if (db.Database.IsNpgsql())
    {
      var rows = await db
        .PlanningRefreshRequests.FromSqlInterpolated(
          $"""
          WITH candidate AS (
            SELECT "Id" FROM "PlanningRefreshRequests"
            WHERE "CompletedVersion" < "RequestedVersion"
              AND "AvailableAt" <= {now}
              AND ("LeaseUntil" IS NULL OR "LeaseUntil" <= {now})
            ORDER BY "AvailableAt", "Id"
            LIMIT 1 FOR UPDATE SKIP LOCKED
          )
          UPDATE "PlanningRefreshRequests" AS request
          SET "LeaseId" = {lease}, "LeaseUntil" = {until},
            "Attempts" = request."Attempts" + 1
          FROM candidate WHERE request."Id" = candidate."Id"
          RETURNING request.*
          """
        )
        .AsNoTracking()
        .ToListAsync(ct);
      work = rows.SingleOrDefault();
    }
    else
    {
      work = await db
        .PlanningRefreshRequests.AsNoTracking()
        .Where(x =>
          x.CompletedVersion < x.RequestedVersion
          && x.AvailableAt <= now
          && (x.LeaseUntil == null || x.LeaseUntil <= now)
        )
        .OrderBy(x => x.AvailableAt)
        .ThenBy(x => x.Id)
        .FirstOrDefaultAsync(ct);
      if (work is null)
        return null;
      var updated = await db
        .PlanningRefreshRequests.Where(x =>
          x.Id == work.Id
          && x.RequestedVersion == work.RequestedVersion
          && x.CompletedVersion < x.RequestedVersion
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
      work.Attempts++;
    }
    return work is null
      ? null
      : new(
        work.Id,
        new(work.DispatchId, work.ExecutionLegId, work.AssignmentRevision),
        work.RequestedVersion,
        lease,
        until,
        work.Attempts
      );
  }

  public async Task<bool> CompleteAsync(
    PlanningRefreshWork work,
    bool succeeded,
    DateTime now,
    DateTime nextAvailable,
    CancellationToken ct
  ) =>
    await db
      .PlanningRefreshRequests.Where(x =>
        x.Id == work.Id && x.LeaseId == work.LeaseId && x.LeaseUntil > now
      )
      .ExecuteUpdateAsync(
        setters =>
          setters
            .SetProperty(
              x => x.CompletedVersion,
              x => succeeded ? work.Version : x.CompletedVersion
            )
            .SetProperty(
              x => x.AvailableAt,
              x =>
                x.RequestedVersion == work.Version
                  ? nextAvailable
                  : x.AvailableAt
            )
            .SetProperty(x => x.LeaseId, (Guid?)null)
            .SetProperty(x => x.LeaseUntil, (DateTime?)null),
        ct
      ) == 1;

  public Task PruneAsync(DateTime before, CancellationToken ct) =>
    db
      .PlanningRefreshRequests.Where(x =>
        x.RequestedAt < before
        && x.CompletedVersion == x.RequestedVersion
        && x.LeaseId == null
        && x.AvailableAt < before
      )
      .ExecuteDeleteAsync(ct);
}
