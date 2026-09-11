using Application.Features.Routing.Services.Routes;
using Application.Features.Routing.Services.Deadheads;
using Application.Features.Routing.Models;
using Application.Features.Routing.Services;
using Application.Features.Routing.Interfaces;
using Application.Features.Routing.Background;
using Application.Models;
using System.Text.Json;
using System.Security.Cryptography;

namespace Application.Features.Routing.Queries;

public sealed record GetNextLoadRoutesQuery(Guid TruckId, Guid? CurrentDispatchId, string? Revision = null)
  : IRequest<RequestResponse<NextLoadRoutesResponse>>;

public sealed class GetNextLoadRoutesHandler(INextLoadRouteReader reader, IDeadheadHistoryReader historyReader,
  RoutePlanningService planning, RoutePreparationQueue preparation)
  : IRequestHandler<GetNextLoadRoutesQuery, RequestResponse<NextLoadRoutesResponse>>
{
  public async Task<RequestResponse<NextLoadRoutesResponse>> Handle(GetNextLoadRoutesQuery request, CancellationToken ct)
  {
    var loads = await reader.ReadLoadsAsync(request.TruckId, ct);
    var upcoming = Algorithms.NextLoadSelection.Select(loads, request.CurrentDispatchId);
    var ids = upcoming.Select(x => x.Id).ToList();
    if (ids.Count == 0) return Respond([]);
    var profile = await planning.ProfileAsync(request.TruckId, ct);
    var history = await historyReader.ReadLoadedAsync(upcoming, ct);
    var signatures = upcoming.ToDictionary(x => x.Id, x => BaseRouteService.Signature(x, profile));
    var connections = upcoming.ToDictionary(x => x.Id, x => DeadheadConnection.Find(history.GetValueOrDefault(x.Id)));
    string GeometryRevision(IEnumerable<NextLoadRouteVersion> versions) => Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(new {
      Policy = 3, request.TruckId, request.CurrentDispatchId, Versions = versions.OrderBy(x => x.DispatchId),
      Loads = upcoming.Select(load => new { load.Id, load.LoadNumber, load.Status,
        Base = signatures[load.Id], Connection = connections[load.Id]?.Signature(profile),
        Stops = load.Stops.OrderBy(s => s.Sequence).Select(s => new { s.Id, s.Job, s.Sequence }) })
    })));
    var labels = upcoming.Select(load => new NextLoadLabels(load.Id,
      load.Stops.OrderBy(s => s.Sequence).Select(s => s.Name).ToList())).ToList();
    if (request.Revision is not null)
    {
      var versions = await reader.ReadVersionsAsync(ids, ct);
      var geometry = GeometryRevision(versions);
      var known = NextLoadRoutesResponse.MetadataRevision(geometry, labels);
      var byId = versions.ToDictionary(x => x.DispatchId);
      for (var index = 0; index < upcoming.Count; index++)
      {
        var load = upcoming[index];
        var version = byId.GetValueOrDefault(load.Id);
        var pair = connections[load.Id];
        if (version?.BaseInputHash != signatures[load.Id] || pair is not null
          && (version?.DeadheadInputHash != pair.Signature(profile) || version?.EmptyMiles is null))
          preparation.Request(load.Id, geometry, index);
      }
      if (request.Revision == known) return RequestResponse<NextLoadRoutesResponse>.Ok(new(known, true, null));
      if (NextLoadRoutesResponse.HasGeometry(request.Revision, geometry))
        return RequestResponse<NextLoadRoutesResponse>.Ok(new(known, false, null, labels));
    }
    var saved = await reader.ReadGeometryAsync(ids, ct);
    var geometryRevision = GeometryRevision(upcoming.Select(load =>
    {
      var item = saved.GetValueOrDefault(load.Id);
      return new NextLoadRouteVersion(load.Id, item?.BaseRoute?.InputHash, item?.BaseRoute?.CalculatedAt,
        item?.Deadhead?.PreviousDispatchId, item?.Deadhead?.InputHash, item?.Deadhead?.CalculatedAt, item?.Deadhead?.Miles);
    }));
    var revision = NextLoadRoutesResponse.MetadataRevision(geometryRevision, labels);
    var previousId = request.CurrentDispatchId;
    var result = new List<NextLoadRoute>();
    foreach (var load in upcoming)
    {
      var entry = saved.GetValueOrDefault(load.Id);
      var pair = connections[load.Id];
      var connection = pair is not null && pair.Previous.Id == previousId ? pair.ReadRoute(entry?.Deadhead, profile) : null;
      var deadhead = connection is null ? null : NextLoadConnection.From(connection);
      previousId = load.Id;
      var route = entry?.BaseRoute is { } baseRoute && baseRoute.InputHash == signatures[load.Id]
        ? SavedRouteReader.Route(baseRoute.RouteJson, load.Stops.Count - 1) : null;
      if (pair is not null && connection is null) preparation.Request(load.Id, geometryRevision, result.Count);
      var stops = load.Stops.OrderBy(s => s.Sequence).ToList();
      if (stops.Count < 2 || route is null)
      {
        preparation.Request(load.Id, geometryRevision, result.Count);
        result.Add(new(load.Id, load.LoadNumber, "pending", [], [], deadhead, stops.Count));
        continue;
      }
      result.Add(new(load.Id, load.LoadNumber, "ready", route.Legs, stops.Select((s, i) =>
      {
        var point = i == 0 ? route.Legs[0].Points[0] : route.Legs[i - 1].Points[^1];
        return new NextLoadStop(point.Latitude, point.Longitude, s.Job, s.Name) { Id = s.Id };
      }).ToList(), deadhead, stops.Count));
    }
    return RequestResponse<NextLoadRoutesResponse>.Ok(new(revision, false, result, labels));

    RequestResponse<NextLoadRoutesResponse> Respond(IReadOnlyList<NextLoadRoute> routes) =>
      RequestResponse<NextLoadRoutesResponse>.Ok(NextLoadRoutesResponse.Create(request.TruckId, request.CurrentDispatchId, routes, request.Revision));
  }
}
