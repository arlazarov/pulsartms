using Application.Diagnostics.Consistency;
using Domain.Rules.Routing;

namespace Application.Features.Routing.Audit;

// A saved road for work whose points are all in one country stays in that
// country (AMF1414, September 25, went through Ontario). The verdict is
// written with the road, or by BaseRoadBorderCheck for older roads, so the
// rules read one short column and never the geometry. Only work that can
// still run is reported. A road not yet checked is reported by neither.
internal static class BaseRoadBorders
{
  public static async Task<ConsistencyPage> ReadAsync(
    IAppDbContext db,
    ConsistencyPageRequest request,
    bool leaves,
    CancellationToken ct
  )
  {
    Guid? after = request.After is null ? null : Guid.Parse(request.After);
    var rows = await db
      .DispatchBaseRoutes.AsNoTracking()
      .Where(x =>
        x.CompanyId == request.Company
        && x.BorderCheck != null
        && (
          leaves
            ? x.BorderCheck != BorderVerdict.StaysValue
              && x.BorderCheck != BorderVerdict.UnknownValue
              && x.BorderCheck != BorderVerdict.NotJudgedValue
            : x.BorderCheck == BorderVerdict.UnknownValue
        )
        && (after == null || x.Id.CompareTo(after.Value) > 0)
        && (
          x.ExecutionLegId == null
          || db.ExecutionLegs.Any(leg =>
            leg.Id == x.ExecutionLegId
            && (leg.Status == "planned" || leg.Status == "active")
          )
        )
      )
      .OrderBy(x => x.Id)
      .Select(x => new
      {
        x.Id,
        x.DispatchId,
        x.ExecutionLegId,
        x.BorderCheck,
        x.CalculatedAt,
        LoadNumber = db
          .Dispatches.Where(load => load.Id == x.DispatchId)
          .Select(load => load.LoadNumber)
          .FirstOrDefault(),
      })
      .Take(request.Limit + 1)
      .ToListAsync(ct);
    return new(
      [
        .. rows.Take(request.Limit)
          .Select(x => new ConsistencyObservation(
            x.Id.ToString(),
            $"calculated:{x.CalculatedAt:O};border:{x.BorderCheck}",
            new Dictionary<string, string>
            {
              ["dispatchId"] = x.DispatchId.ToString(),
              ["loadNumber"] = x.LoadNumber.ToString(),
              ["executionLegId"] = x.ExecutionLegId?.ToString() ?? "none",
              ["border"] = x.BorderCheck!,
              ["calculatedAt"] = x.CalculatedAt.ToString("O"),
            }
          )),
      ],
      rows.Count > request.Limit
    );
  }
}

public sealed class BaseRoadBorderRule(IAppDbContext db) : IConsistencyRule
{
  public ConsistencyRuleInfo Info { get; } =
    new(
      "routing.base-road-leaves-country",
      1,
      "Routing: BaseRouteService",
      ConsistencyCondition.Violation,
      ConsistencySeverity.Warning,
      "A saved road whose stops are all in one country stays in it.",
      "Build the load's route again; only this road is bought again. If the "
        + "new road is refused too, add a waypoint in the load's country."
    );

  public Task<ConsistencyPage> ReadAsync(
    ConsistencyPageRequest request,
    CancellationToken ct
  ) => BaseRoadBorders.ReadAsync(db, request, leaves: true, ct);
}

// A road the check could not fully place (a point off every country, bad
// or missing geometry) is not known to stay; it is left for a person.
public sealed class BaseRoadBorderUnknownRule(IAppDbContext db)
  : IConsistencyRule
{
  public ConsistencyRuleInfo Info { get; } =
    new(
      "routing.base-road-border-unverified",
      1,
      "Routing: BaseRouteService",
      ConsistencyCondition.Review,
      ConsistencySeverity.Warning,
      "A saved road for one-country work whose border could not be "
        + "verified is reviewed.",
      "Open the load's route and check where it goes; build it again if "
        + "it leaves the country or the road is missing."
    );

  public Task<ConsistencyPage> ReadAsync(
    ConsistencyPageRequest request,
    CancellationToken ct
  ) => BaseRoadBorders.ReadAsync(db, request, leaves: false, ct);
}
