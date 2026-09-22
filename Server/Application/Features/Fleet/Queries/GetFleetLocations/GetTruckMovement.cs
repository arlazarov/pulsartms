using System.Text.Json;
using Application.Models;
using Domain.Models.Routing;
using Domain.Rules;

namespace Application.Features.Fleet.Queries.GetFleetLocations;

public sealed record GetTruckMovementQuery(
  Guid TruckId,
  DateTimeOffset From,
  DateTimeOffset To
) : IRequest<RequestResponse<TruckMovementHistory>>;

public sealed record RecordedTruckMovement(
  Guid RoutePlanId,
  RouteMovement Movement,
  bool Open
);

public sealed record TruckMovementHistory(
  IReadOnlyList<RecordedTruckMovement> Segments,
  bool Truncated
);

public sealed class GetTruckMovementHandler(IAppDbContext db)
  : IRequestHandler<
    GetTruckMovementQuery,
    RequestResponse<TruckMovementHistory>
  >
{
  public async Task<RequestResponse<TruckMovementHistory>> Handle(
    GetTruckMovementQuery request,
    CancellationToken ct
  )
  {
    var from = request.From.UtcDateTime;
    var to = request.To.UtcDateTime;
    if (to <= from || to - from > TimeSpan.FromHours(25))
      return RequestResponse<TruckMovementHistory>.Fail(
        "Choose a movement history window of at most 25 hours."
      );
    var closedQuery = db
      .RouteMovementChunks.AsNoTracking()
      .Where(x => x.TruckId == request.TruckId && x.To >= from && x.From <= to)
      .OrderBy(x => x.From)
      .ThenBy(x => x.Id)
      .Take(513)
      .Select(x => new
      {
        x.RoutePlanId,
        Payload = x.MovementJson,
        Open = false,
      });
    var currentQuery = db
      .DispatchRoutePlans.AsNoTracking()
      .Where(x =>
        x.TruckId == request.TruckId && x.GeometryManifestJson != null
      )
      .OrderByDescending(x => x.CreatedAt)
      .Take(33)
      .Select(x => new
      {
        RoutePlanId = x.Id,
        Payload = x.PlanJson,
        Open = true,
      });
    // One statement cannot lose a segment between its open checkpoint and
    // the atomic publication of its closed record.
    var rows = await closedQuery.Concat(currentQuery).ToListAsync(ct);
    var closed = rows.Where(x => !x.Open).ToList();
    var current = rows.Where(x => x.Open).ToList();

    var result = closed
      .Take(512)
      .Select(x => new RecordedTruckMovement(
        x.RoutePlanId,
        JsonSerializer.Deserialize<RouteMovement>(
          x.Payload,
          RoutingJson.Options
        )!,
        false
      ))
      .ToList();
    foreach (var row in current.Take(32))
    {
      var plan = JsonSerializer.Deserialize<RoutePlan>(
        row.Payload,
        RoutingJson.Options
      );
      if (
        plan?.Tracking.Movement is { Observations.Count: > 0 } movement
        && movement.Observations[0].At <= to
        && movement.Observations[^1].At >= from
      )
        result.Add(new(row.RoutePlanId, movement, true));
    }
    return RequestResponse<TruckMovementHistory>.Ok(
      new(result, closed.Count > 512 || current.Count > 32)
    );
  }
}
