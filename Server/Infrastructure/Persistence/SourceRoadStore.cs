using System.Security.Cryptography;
using System.Text;
using Application.Features.Routing.Interfaces;
using Domain.Entities.Dispatch;
using Domain.Models.Routing;
using Microsoft.EntityFrameworkCore;

namespace Infrastructure.Persistence;

public sealed class SourceRoadStore(AppDbContext db) : ISourceRoadStore
{
  // A raw statement goes around the stamp, so it names the carrier itself.
  private Guid Company() =>
    db.ServingCompany
    ?? throw new InvalidOperationException(
      "Work cannot be queued without a company."
    );

  public Task ObserveAsync(
    Guid dispatchId,
    Guid? truckId,
    string inputSignature,
    int priority,
    bool explicitlyRequested,
    bool refreshCompleted,
    DateTime now,
    CancellationToken ct
  ) =>
    RequestAsync(
      dispatchId,
      truckId,
      inputSignature,
      "",
      priority,
      explicitlyRequested,
      refreshCompleted,
      now,
      ct
    );

  public Task DemandAsync(
    Guid dispatchId,
    string identity,
    int priority,
    DateTime now,
    CancellationToken ct
  ) =>
    RequestAsync(
      dispatchId,
      null,
      "",
      Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(identity))),
      priority,
      true,
      false,
      now,
      ct
    );

  private Task RequestAsync(
    Guid dispatchId,
    Guid? truckId,
    string input,
    string demand,
    int priority,
    bool explicitlyRequested,
    bool refreshCompleted,
    DateTime now,
    CancellationToken ct
  )
  {
    if (db.Database.CurrentTransaction is not null)
      throw new InvalidOperationException(
        "Source-road demand requires an independent commit."
      );
    return db.Database.ExecuteSqlInterpolatedAsync(
      $"""
      INSERT INTO "SourceRoadRequests" AS request (
        "DispatchId", "CompanyId", "TruckId", "InputSignature", "DemandIdentity", "Explicit",
        "Priority", "RequestedVersion", "CompletedVersion", "RequestedAt",
        "AvailableAt", "Attempts"
      ) VALUES (
        {dispatchId}, {Company()}, {truckId}, {input}, {demand}, {explicitlyRequested},
        {priority}, 1, 0, {now}, {now}, 0
      ) ON CONFLICT ("DispatchId") DO UPDATE SET
        "TruckId" = CASE WHEN EXCLUDED."InputSignature" = ''
          THEN request."TruckId" ELSE EXCLUDED."TruckId" END,
        "InputSignature" = CASE WHEN EXCLUDED."InputSignature" = ''
          THEN request."InputSignature" ELSE EXCLUDED."InputSignature" END,
        "DemandIdentity" = CASE WHEN EXCLUDED."DemandIdentity" = ''
          THEN request."DemandIdentity" ELSE EXCLUDED."DemandIdentity" END,
        "Explicit" = request."Explicit" OR EXCLUDED."Explicit",
        "Priority" = CASE
          WHEN request."CompletedVersion" = request."RequestedVersion"
            OR EXCLUDED."Priority" < request."Priority"
          THEN EXCLUDED."Priority" ELSE request."Priority" END,
        "RequestedVersion" = request."RequestedVersion" + CASE
          WHEN request."CompletedVersion" < request."RequestedVersion"
            AND (EXCLUDED."InputSignature" = ''
              OR request."InputSignature" = ''
              OR request."InputSignature" = EXCLUDED."InputSignature")
            AND (EXCLUDED."DemandIdentity" = ''
              OR request."DemandIdentity" = ''
              OR request."DemandIdentity" = EXCLUDED."DemandIdentity")
          THEN 0 ELSE 1 END,
        "RequestedAt" = EXCLUDED."RequestedAt",
        "AvailableAt" = CASE
          WHEN request."CompletedVersion" < request."RequestedVersion"
            AND (EXCLUDED."InputSignature" = ''
              OR request."InputSignature" = ''
              OR request."InputSignature" = EXCLUDED."InputSignature")
            AND (EXCLUDED."DemandIdentity" = ''
              OR request."DemandIdentity" = ''
              OR request."DemandIdentity" = EXCLUDED."DemandIdentity")
          THEN request."AvailableAt" ELSE EXCLUDED."AvailableAt" END,
        "Attempts" = CASE
          WHEN request."CompletedVersion" < request."RequestedVersion"
            AND (EXCLUDED."InputSignature" = ''
              OR request."InputSignature" = ''
              OR request."InputSignature" = EXCLUDED."InputSignature")
            AND (EXCLUDED."DemandIdentity" = ''
              OR request."DemandIdentity" = ''
              OR request."DemandIdentity" = EXCLUDED."DemandIdentity")
          THEN request."Attempts" ELSE 0 END
      WHERE (EXCLUDED."InputSignature" <> ''
          AND request."InputSignature" <> EXCLUDED."InputSignature")
        OR (EXCLUDED."DemandIdentity" <> ''
          AND request."DemandIdentity" <> EXCLUDED."DemandIdentity")
        OR (EXCLUDED."Explicit" AND NOT request."Explicit")
        OR EXCLUDED."Priority" < request."Priority"
        OR (request."CompletedVersion" = request."RequestedVersion"
          AND ({refreshCompleted} OR request."AvailableAt" <= {now}))
      """,
      ct
    );
  }

  public async Task<SourceRoadWork?> ClaimAsync(
    DateTime now,
    TimeSpan leaseDuration,
    CancellationToken ct
  )
  {
    var lease = Guid.NewGuid();
    var until = now.Add(leaseDuration);
    SourceRoadRequest? work;
    if (db.Database.IsNpgsql())
    {
      // Deliberately across carriers. This is the server's work list,
      // not one carrier's: a worker claims whatever is next and then
      // runs that pass as the carrier the claimed row belongs to.
      var rows = await db
        .SourceRoadRequests.FromSqlInterpolated(
          $"""
          WITH candidate AS (
            SELECT "DispatchId" FROM "SourceRoadRequests"
            WHERE "CompletedVersion" < "RequestedVersion"
              AND "AvailableAt" <= {now}
              AND ("LeaseUntil" IS NULL OR "LeaseUntil" <= {now})
            ORDER BY "Priority", "AvailableAt", "DispatchId"
            LIMIT 1 FOR UPDATE SKIP LOCKED
          )
          UPDATE "SourceRoadRequests" AS request
          SET "LeaseId" = {lease}, "LeaseUntil" = {until},
            "Attempts" = request."Attempts" + 1
          FROM candidate WHERE request."DispatchId" = candidate."DispatchId"
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
        .SourceRoadRequests.AsNoTracking()
        .Where(x =>
          x.CompletedVersion < x.RequestedVersion
          && x.AvailableAt <= now
          && (x.LeaseUntil == null || x.LeaseUntil <= now)
        )
        .OrderBy(x => x.Priority)
        .ThenBy(x => x.AvailableAt)
        .ThenBy(x => x.DispatchId)
        .FirstOrDefaultAsync(ct);
      if (work is null)
        return null;
      var updated = await db
        .SourceRoadRequests.Where(x =>
          x.DispatchId == work.DispatchId
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
        work.DispatchId,
        work.CompanyId,
        work.TruckId,
        work.RequestedVersion,
        lease,
        until,
        work.Attempts,
        work.Explicit
      );
  }

  public async Task<bool> CompleteAsync(
    SourceRoadWork work,
    bool succeeded,
    DateTime now,
    DateTime nextAvailable,
    CancellationToken ct
  ) =>
    await db
      .SourceRoadRequests.Where(x =>
        x.DispatchId == work.DispatchId
        && x.LeaseId == work.LeaseId
        && x.LeaseUntil > now
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
      .SourceRoadRequests.Where(x =>
        x.RequestedAt < before
        && x.CompletedVersion == x.RequestedVersion
        && x.LeaseId == null
        && x.AvailableAt < before
      )
      .ExecuteDeleteAsync(ct);
}
