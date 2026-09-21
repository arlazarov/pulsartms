using Application.Features.Routing.Background;
using Application.Features.Routing.Interfaces;
using Application.Features.Routing.Services.Routes;
using Application.Models;
using Domain.Models.Routing;
using Domain.Rules;
using Domain.Rules.Routing;

namespace Application.Features.Routing.Queries;

public sealed record GetDispatchMapRouteQuery(Guid DispatchId)
  : IRequest<RequestResponse<DispatchMapRoute>>;

public sealed class GetDispatchMapRouteHandler(
  IAppDbContext db,
  RoutePlanningService planning,
  TruckPlanningProfileService profiles,
  SourceRoadDemand preparation
) : IRequestHandler<GetDispatchMapRouteQuery, RequestResponse<DispatchMapRoute>>
{
  public async Task<RequestResponse<DispatchMapRoute>> Handle(
    GetDispatchMapRouteQuery request,
    CancellationToken ct
  )
  {
    var source = await db
      .Dispatches.AsNoTracking()
      .Include(x => x.Stops)
      .SingleOrDefaultAsync(x => x.Id == request.DispatchId, ct);
    if (source is null)
      return RequestResponse<DispatchMapRoute>.Fail("Load not found.", 404);
    var native = await ExecutionRouteSections.ReadAsync(db, [source], ct);
    var sections = new List<RouteWorkSnapshot>();
    if (native.TryGetValue(source.Id, out var legs))
      sections.AddRange(legs);
    else
    {
      try
      {
        sections.Add(
          await planning.ResolveAssignmentAsync(
            source,
            ct,
            confirmedLegacy: true
          )
        );
      }
      catch (RoutePlanningException)
      {
        return RequestResponse<DispatchMapRoute>.Ok(new(source.Id, [], 1));
      }
    }
    var saved = await db
      .DispatchBaseRoutes.AsNoTracking()
      .Where(x => x.DispatchId == source.Id)
      .ToListAsync(ct);
    var segments = new List<DispatchMapSegment>();
    var missing = 0;
    var pointCount = 0;
    var inputs = new List<string>();
    foreach (var section in sections)
    {
      var stops = section.Stops.OrderBy(x => x.Sequence).ToArray();
      if (stops.Length < 2)
        continue;
      var entity = saved.SingleOrDefault(x =>
        x.ExecutionLegId == section.ExecutionLegId
      );
      if (section.TruckId is not { } truckId)
      {
        missing++;
        continue;
      }
      var profile = await profiles.GetAsync(truckId, ct);
      var signature = BaseRouteService.Signature(section, profile);
      inputs.Add(signature);
      var route =
        entity?.InputHash == signature
          ? SavedRouteReader.Route(entity.RouteJson, stops.Length - 1)
          : null;
      var anchors = stops
        .Select(x => new RoutePoint(
          (double?)x.Latitude ?? double.NaN,
          (double?)x.Longitude ?? double.NaN
        ))
        .ToArray();
      if (
        route is null
        || !RouteAnchoring.Matches(route, anchors)
        || route.Legs.Sum(x => (long)x.Points.Count) + pointCount > 200_000
      )
      {
        missing++;
        continue;
      }
      for (var i = 0; i < route.Legs.Count; i++)
      {
        var points = route.Legs[i].Points;
        pointCount += points.Count;
        segments.Add(
          new(stops[i].Id, stops[i + 1].Id, points)
          {
            Meaning = RouteSegmentClassification.FromState(stops[i].StateAfter),
          }
        );
      }
    }
    if (missing > 0 && inputs.Count > 0)
      await preparation.RequestAsync(
        source.Id,
        string.Join("|", inputs),
        0,
        ct
      );
    return RequestResponse<DispatchMapRoute>.Ok(
      new(source.Id, segments, missing)
    );
  }
}
