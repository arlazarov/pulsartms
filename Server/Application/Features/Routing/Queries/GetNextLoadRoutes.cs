using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using Application.Diagnostics;
using Application.Features.Execution.Queries;
using Application.Features.Routing.Background;
using Application.Features.Routing.Interfaces;
using Application.Features.Routing.Services;
using Application.Features.Routing.Services.Deadheads;
using Application.Features.Routing.Services.Routes;
using Application.Models;
using Domain.Models.Execution;
using Domain.Models.Routing;
using Domain.Rules;
using Domain.Rules.Routing;

namespace Application.Features.Routing.Queries;

public sealed record GetNextLoadRoutesQuery(
  Guid TruckId,
  Guid? CurrentDispatchId,
  string? Revision = null,
  Guid? CurrentExecutionLegId = null
) : IRequest<RequestResponse<NextLoadRoutesResponse>>;

public sealed class GetNextLoadRoutesHandler(
  INextLoadRouteReader reader,
  TruckPlanningInputsReader inputs,
  DeadheadHistoryService historyReader,
  RoutePlanningService planning,
  SourceRoadDemand preparation
)
  : IRequestHandler<
    GetNextLoadRoutesQuery,
    RequestResponse<NextLoadRoutesResponse>
  >
{
  public async Task<RequestResponse<NextLoadRoutesResponse>> Handle(
    GetNextLoadRoutesQuery request,
    CancellationToken ct
  )
  {
    var started = Stopwatch.GetTimestamp();
    // The truck's current work and the work after it are the planning
    // inputs' - the same choice and the same order (WorkOrderKey) the
    // board, the summary and the ETA use. A current work the client names
    // that the inputs do not is either the client's or this process' cache
    // being behind: inputs older than a moment are captured once more, and
    // if they still disagree the client is told to refresh. Nothing is
    // guessed from the client's value, and a client polling with a stale
    // value costs at most one capture per moment.
    var seen = inputs.Version(request.TruckId);
    var work = await inputs.ReadAsync(request.TruckId, ct, includeHos: false);
    if (
      work is not null
      && !Names(work, request)
      && !inputs.CapturedWithin(work, RecaptureAge)
    )
      work = await inputs.ReadAgainAsync(
        request.TruckId,
        seen,
        ct,
        includeHos: false
      );
    started = Mark("inputs", started);
    if (work?.CurrentSegment is null)
      return Respond([]);
    if (!Names(work, request))
      return RequestResponse<NextLoadRoutesResponse>.Fail(
        "The current truck assignment changed. Refresh its route.",
        409
      );
    var truckNumber = work.Itinerary.Resources.TruckNumber;
    var upcoming = work
      .Followers.Select(x => RouteWorkProjection.Capture(x, truckNumber))
      .ToList();
    var ids = upcoming
      .Where(x => !x.ExecutionLegId.HasValue)
      .Select(x => x.Id)
      .ToArray();
    var legs = upcoming
      .Where(x => x.ExecutionLegId.HasValue)
      .Select(x => x.ExecutionLegId!.Value)
      .ToArray();
    if (upcoming.Count == 0)
      return Respond([]);
    started = Stopwatch.GetTimestamp();
    var profile = await planning.ProfileAsync(request.TruckId, ct);
    started = Mark("profile", started);
    var history = await historyReader.ReadSectionsAsync(upcoming.ToArray(), ct);
    started = Mark("history", started);
    var signatures = upcoming.ToDictionary(
      Key,
      x => BaseRouteService.Signature(x, profile)
    );
    var connections = upcoming.ToDictionary(
      Key,
      x => DeadheadConnection.Find(history.GetValueOrDefault(Key(x)))
    );
    string GeometryRevision(IEnumerable<NextLoadRouteVersion> versions) =>
      Convert.ToHexString(
        SHA256.HashData(
          JsonSerializer.SerializeToUtf8Bytes(
            new
            {
              Policy = 3,
              request.TruckId,
              request.CurrentDispatchId,
              request.CurrentExecutionLegId,
              Versions = versions
                .OrderBy(x => x.DispatchId)
                .ThenBy(x => x.ExecutionLegId),
              Loads = upcoming.Select(load => new
              {
                load.Id,
                load.ExecutionLegId,
                load.AssignmentRevision,
                load.LoadNumber,
                load.Status,
                Base = signatures[Key(load)],
                Connection = connections[Key(load)]?.Signature(profile),
                Stops = load
                  .Stops.OrderBy(s => s.Sequence)
                  .Select(s => new
                  {
                    s.Id,
                    s.Job,
                    s.Sequence,
                    s.StateAfter,
                    s.OperationRevision,
                  }),
              }),
            }
          )
        )
      );
    var labels = upcoming
      .Select(load => new NextLoadLabels(
        load.Id,
        load.Stops.OrderBy(s => s.Sequence).Select(s => s.Name).ToList()
      )
      {
        ExecutionLegId = load.ExecutionLegId,
      })
      .ToList();
    if (request.Revision is not null)
    {
      started = Stopwatch.GetTimestamp();
      var versions = await VersionsAsync();
      started = Mark("versions", started);
      var geometry = GeometryRevision(versions);
      var known = NextLoadRoutesResponse.MetadataRevision(geometry, labels);
      var byId = versions.ToDictionary(x => (x.DispatchId, x.ExecutionLegId));
      for (var index = 0; index < upcoming.Count; index++)
      {
        var load = upcoming[index];
        var version = byId.GetValueOrDefault(Key(load));
        var pair = connections[Key(load)];
        if (
          version?.BaseInputHash != signatures[Key(load)]
          || pair is not null
            && (
              version?.DeadheadInputHash != pair.Signature(profile)
              || version?.EmptyMiles is null
            )
        )
          await preparation.RequestAsync(load.Id, geometry, index, ct);
      }
      if (request.Revision == known)
        return RequestResponse<NextLoadRoutesResponse>.Ok(
          new(known, true, null)
        );
      if (NextLoadRoutesResponse.HasGeometry(request.Revision, geometry))
        return RequestResponse<NextLoadRoutesResponse>.Ok(
          new(known, false, null, labels)
        );
    }
    started = Stopwatch.GetTimestamp();
    var saved = await GeometryAsync();
    started = Mark("geometry", started);
    var geometryRevision = GeometryRevision(
      upcoming.Select(load =>
        NextLoadRouteVersion.From(
          load.Id,
          load.ExecutionLegId,
          saved.GetValueOrDefault(Key(load))
        )
      )
    );
    var revision = NextLoadRoutesResponse.MetadataRevision(
      geometryRevision,
      labels
    );
    var previousId = request.CurrentDispatchId;
    var previousLeg = request.CurrentExecutionLegId;
    var result = new List<NextLoadRoute>();
    foreach (var load in upcoming)
    {
      var entry = saved.GetValueOrDefault(Key(load));
      var pair = connections[Key(load)];
      var connection =
        pair is not null
        && pair.Previous.ExecutionLegId == previousLeg
        && pair.Previous.Id == previousId
          ? pair.ReadRoute(entry?.Deadhead, profile)
          : null;
      var deadhead = connection is null
        ? null
        : NextLoadConnection.From(connection);
      // These roads are only drawn. The saved geometry is what the routing
      // provider returned, several times denser than a line on a map needs,
      // and three upcoming loads of it was the largest thing the map was
      // ever sent.
      if (deadhead is not null)
        deadhead = deadhead with
        {
          Points = DisplayRouteGeometry.Simplify(deadhead.Points),
        };
      previousId = load.Id;
      previousLeg = load.ExecutionLegId;
      var route =
        entry?.BaseRoute is { } baseRoute
        && baseRoute.ExecutionLegId == load.ExecutionLegId
        && baseRoute.InputHash == signatures[Key(load)]
          ? SavedRouteReader.Route(baseRoute.RouteJson, load.Stops.Length - 1)
          : null;
      if (pair is not null && connection is null)
        await preparation.RequestAsync(
          load.Id,
          geometryRevision,
          result.Count,
          ct
        );
      var stops = load.Stops.OrderBy(s => s.Sequence).ToList();
      if (stops.Count < 2 || route is null)
      {
        await preparation.RequestAsync(
          load.Id,
          geometryRevision,
          result.Count,
          ct
        );
        result.Add(
          new(
            load.Id,
            load.LoadNumber,
            "pending",
            [],
            [],
            deadhead,
            stops.Count
          )
          {
            ExecutionLegId = load.ExecutionLegId,
          }
        );
        continue;
      }
      result.Add(
        new(
          load.Id,
          load.LoadNumber,
          "ready",
          route
            .Legs.Select(leg =>
              leg with
              {
                Points = DisplayRouteGeometry.Simplify(leg.Points),
              }
            )
            .ToList(),
          stops
            .Select(
              (s, i) =>
              {
                var point =
                  i == 0
                    ? route.Legs[0].Points[0]
                    : route.Legs[i - 1].Points[^1];
                return new NextLoadStop(
                  point.Latitude,
                  point.Longitude,
                  s.Job,
                  s.Name
                )
                {
                  Id = s.Id,
                  StateAfter = s.StateAfter,
                  OperationRevision = s.OperationRevision,
                };
              }
            )
            .ToList(),
          deadhead,
          stops.Count
        )
        {
          ExecutionLegId = load.ExecutionLegId,
        }
      );
    }
    Mark("assemble", started);
    return RequestResponse<NextLoadRoutesResponse>.Ok(
      new(revision, false, result, labels)
    );

    static long Mark(string stage, long since)
    {
      PerformanceStages.Elapsed("next-routes", stage, since);
      return Stopwatch.GetTimestamp();
    }

    async Task<List<NextLoadRouteVersion>> VersionsAsync()
    {
      List<NextLoadRouteVersion> values = [];
      if (ids.Length > 0)
        values.AddRange(await reader.ReadVersionsAsync(ids, ct));
      if (legs.Length > 0)
        values.AddRange(await reader.ReadExecutionVersionsAsync(legs, ct));
      return values;
    }

    async Task<Dictionary<(Guid, Guid?), SavedNextLoadRoute>> GeometryAsync()
    {
      var values = new Dictionary<(Guid, Guid?), SavedNextLoadRoute>();
      if (ids.Length > 0)
        foreach (var saved in (await reader.ReadGeometryAsync(ids, ct)).Values)
          values[(saved.DispatchId, null)] = saved;
      if (legs.Length > 0)
        foreach (
          var saved in (
            await reader.ReadExecutionGeometryAsync(legs, ct)
          ).Values
        )
          values[(saved.DispatchId, saved.ExecutionLegId)] = saved;
      return values;
    }

    RequestResponse<NextLoadRoutesResponse> Respond(
      IReadOnlyList<NextLoadRoute> routes
    ) =>
      RequestResponse<NextLoadRoutesResponse>.Ok(
        NextLoadRoutesResponse.Create(
          request.TruckId,
          request.CurrentDispatchId,
          routes,
          request.Revision
        )
      );
  }

  private static readonly TimeSpan RecaptureAge = TimeSpan.FromSeconds(5);

  private static bool Names(
    TruckPlanningInputs work,
    GetNextLoadRoutesQuery request
  ) =>
    work.CurrentWork
    == new WorkIdentity(
      request.CurrentDispatchId ?? Guid.Empty,
      request.CurrentExecutionLegId
    );

  private static (Guid, Guid?) Key(RouteWorkSnapshot load) =>
    (load.Id, load.ExecutionLegId);
}
