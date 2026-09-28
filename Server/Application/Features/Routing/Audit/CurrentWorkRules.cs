using System.Security.Cryptography;
using System.Text;
using Application.Diagnostics.Consistency;
using Application.Features.Routing.Services.Routes;

namespace Application.Features.Routing.Audit;

// CW1 of docs/architecture/current-work.md: work planning has passed is
// finished by execution. The owner of the conflict is the truck's planning
// inputs (WorkPlacements.Conflicts, the board's and the map's answer); this
// asks it for every active truck of the company, in batches of the reader,
// and never decides the conflict itself. A load passed without its
// delivery recorded, and one delivered with a later stop (a trailer drop)
// open, are the dispatcher's to act on: reviews, since GPS passage is not
// delivery. The conflict kind is in the versions, so a load moving from
// one to the other is the same finding observed again.
//
// Unlike the SQL rules, a page is several batched reads through the
// owner's capture, not one statement; the pages are sorted by the
// finding's key after the whole fleet is asked, so the cursor always
// moves. Warm reads come from the owner's read cache.
public sealed class RoutePassedWorkOpenRule(
  IAppDbContext db,
  TruckPlanningInputsReader inputs
) : IConsistencyRule
{
  private const int Batch = 100;

  public ConsistencyRuleInfo Info { get; } =
    new(
      "routing.route-passed-work-open",
      1,
      "Routing: TruckPlanningInputs (WorkPlacements)",
      ConsistencyCondition.Review,
      ConsistencySeverity.Warning,
      "Work planning has passed has its delivery recorded and the truck's "
        + "later stops done.",
      "Open the load in Dispatch: record the delivery, or finish the "
        + "truck's remaining stop."
    );

  public async Task<ConsistencyPage> ReadAsync(
    ConsistencyPageRequest request,
    CancellationToken ct
  )
  {
    var trucks = await db
      .Trucks.AsNoTracking()
      .Where(x => x.CompanyId == request.Company && x.IsActive)
      .OrderBy(x => x.Id)
      .Select(x => x.Id)
      .ToListAsync(ct);
    var observed = new List<ConsistencyObservation>();
    foreach (var batch in trucks.Chunk(Batch))
    foreach (
      var (truck, work) in await inputs.ReadManyAsync(
        batch,
        ct,
        includeHos: false
      )
    )
    foreach (var conflict in WorkPlacements.Conflicts(work))
      observed.Add(
        new(
          $"{conflict.DispatchId:N}:{conflict.ExecutionLegId:N}",
          $"conflict:{conflict.Conflict};"
            + $"itinerary:{work.Itinerary.InputSignature}",
          new Dictionary<string, string>
          {
            ["truckId"] = truck.ToString(),
            ["dispatchId"] = conflict.DispatchId.ToString(),
            ["loadNumber"] = conflict.LoadNumber.ToString(),
            ["executionLegId"] = conflict.ExecutionLegId?.ToString() ?? "none",
            ["conflict"] = conflict.Conflict,
          }
        )
      );
    var page = observed
      .Where(x =>
        request.After is null
        || string.CompareOrdinal(x.EntityKey, request.After) > 0
      )
      .OrderBy(x => x.EntityKey, StringComparer.Ordinal)
      .Take(request.Limit + 1)
      .ToList();
    return new([.. page.Take(request.Limit)], page.Count > request.Limit);
  }
}

// CW4: a stored truck summary speaks for the truck's current work as its
// owner chooses it. The summary's signature names that choice, so an entry
// under the current signature naming another load is a publication fault,
// not staleness; an entry under an older signature is retired by the cache
// itself and is not reported. The summaries are per process: this checks
// the auditing process' own, and says nothing about another's.
public sealed class SummaryNamesCurrentWorkRule(
  PlanningSummaryCache summaries,
  PlanningSummaryReader reader,
  TruckPlanningInputsReader inputs
) : IConsistencyRule
{
  public ConsistencyRuleInfo Info { get; } =
    new(
      "routing.summary-names-current-work",
      1,
      "Routing: PlanningSummaryCache",
      ConsistencyCondition.Violation,
      ConsistencySeverity.Warning,
      "A stored planning summary names the current work its signature was "
        + "prepared for (this process' summaries only).",
      "Check PlanningSummaryOperation logs for the truck; the summary is "
        + "prepared again on the next change of its inputs."
    );

  public async Task<ConsistencyPage> ReadAsync(
    ConsistencyPageRequest request,
    CancellationToken ct
  )
  {
    var stored = summaries
      .StoredFor(request.Company)
      .Where(x =>
        request.After is null
        || string.CompareOrdinal($"{x.Truck:N}", request.After) > 0
      )
      .OrderBy(x => $"{x.Truck:N}", StringComparer.Ordinal)
      .ToList();
    if (stored.Count == 0)
      return new([], false);
    var fresh = await inputs.ReadManyAsync(
      [.. stored.Select(x => x.Truck)],
      ct,
      includeHos: false
    );
    var observed = new List<ConsistencyObservation>();
    foreach (var entry in stored)
    {
      if (
        fresh.GetValueOrDefault(entry.Truck) is not { } work
        || reader.Signature(work) != entry.Signature
        || entry.DispatchId == work.CurrentWork?.DispatchId
      )
        continue;
      observed.Add(
        new(
          $"{entry.Truck:N}",
          $"signature:{Hash(entry.Signature)}",
          new Dictionary<string, string>
          {
            ["truckId"] = entry.Truck.ToString(),
            ["storedDispatchId"] = entry.DispatchId?.ToString() ?? "none",
            ["currentDispatchId"] =
              work.CurrentWork?.DispatchId.ToString() ?? "none",
          }
        )
      );
      if (observed.Count > request.Limit)
        break;
    }
    return new(
      [.. observed.Take(request.Limit)],
      observed.Count > request.Limit
    );
  }

  private static string Hash(string value) =>
    Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)))[..16];
}
