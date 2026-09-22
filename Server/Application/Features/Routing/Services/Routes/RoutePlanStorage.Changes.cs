using System.Text.Json;
using Domain.Entities.Dispatch;
using Domain.Models.Routing;
using Domain.Rules;
using Domain.Rules.Routing;

namespace Application.Features.Routing.Services.Routes;

public static partial class RoutePlanStorage
{
  private sealed record Splice(
    string Road,
    int Leg,
    int At,
    List<RouteChunkRange> Removed,
    List<RouteChunkRange> Inserted
  );

  private static void RecordChange(
    IAppDbContext db,
    DispatchRoutePlan entity,
    RoutePlan plan,
    RouteChunkManifest next,
    string json
  )
  {
    if (entity.GeometryManifestJson == json)
      return;
    var old = entity.GeometryManifestJson is null
      ? null
      : Manifest(entity.GeometryManifestJson);
    if (
      old is not null
      && SavedRouteReader.Plan(entity.PlanJson) is { } prior
      && plan.Version <= prior.Version
    )
      plan.Version = checked(prior.Version + 1);
    if (
      old is not null
      && plan.FuelPlan is { } fuel
      && fuel.RouteVersion != plan.Version
    )
      fuel.NeedsRefresh = true;
    var changes = new List<Splice>();
    Diff("route", old?.Route ?? [], next.Route);
    Diff("reference", old?.Reference ?? [], next.Reference ?? []);
    RouteMovementRecorder.Close(plan);
    entity.GeometryRevision++;
    db.RouteGeometryChanges.Add(
      new()
      {
        CompanyId = entity.CompanyId,
        RoutePlanId = entity.Id,
        Revision = entity.GeometryRevision,
        RecordedAt = DateTime.UtcNow,
        ChangesJson = JsonSerializer.Serialize(
          new
          {
            Format = 1,
            plan.Version,
            plan.CalculatedAt,
            entity.InputHash,
            plan.Profile,
            plan.FromCurrentPosition,
            RouteLegs = next.Route.Count,
            ReferenceLegs = next.Reference?.Count,
            next.Measures,
            next.ReferenceMeasures,
            plan.AssignmentRevision,
            plan.LastReroutedAt,
            plan.LastReroutePosition,
            Kind = old is null ? "Initial" : "Replacement",
            GeometrySource = "EstimatedRoad",
            Legs = plan.Route.Legs.Select(x => new { x.Miles, x.Seconds }),
            Stops = plan.Stops.Select(x => x.Id),
            Changes = changes,
          },
          RoutingJson.Options
        ),
      }
    );

    void Diff(
      string road,
      List<List<RouteChunkRange>> before,
      List<List<RouteChunkRange>> after
    )
    {
      for (var i = 0; i < Math.Max(before.Count, after.Count); i++)
      {
        var a = i < before.Count ? before[i] : [];
        var b = i < after.Count ? after[i] : [];
        var start = 0;
        while (start < a.Count && start < b.Count && a[start] == b[start])
          start++;
        var end = 0;
        while (
          end < a.Count - start
          && end < b.Count - start
          && a[^(end + 1)] == b[^(end + 1)]
        )
          end++;
        if (start == a.Count && start == b.Count)
          continue;
        changes.Add(
          new(
            road,
            i,
            start,
            a.GetRange(start, a.Count - start - end),
            b.GetRange(start, b.Count - start - end)
          )
        );
      }
    }
  }
}
