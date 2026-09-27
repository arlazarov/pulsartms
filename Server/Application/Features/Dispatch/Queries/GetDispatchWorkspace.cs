using Application.Features.Dispatch.Models;
using Application.Features.Dispatch.Services;
using Application.Features.Eta.Services;
using Application.Features.Execution.Models;
using Application.Features.Execution.Services;
using Application.Features.Routing.Services.Deadheads;
using Application.Features.Routing.Services.Routes;
using Application.Models;
using Domain.Models.Eta;
using Domain.Rules.Routing;

namespace Application.Features.Dispatch.Queries;

public sealed record GetDispatchWorkspaceQuery(Guid DispatchId)
  : IRequest<RequestResponse<DispatchWorkspaceResponse>>;

public sealed class GetDispatchWorkspaceHandler(
  IAppDbContext db,
  ICurrentUser caller,
  IUserRoleService roles,
  EtaForecastService eta,
  DeadheadService deadhead,
  TruckPlanningInputsReader inputs
)
  : IRequestHandler<
    GetDispatchWorkspaceQuery,
    RequestResponse<DispatchWorkspaceResponse>
  >
{
  public async Task<RequestResponse<DispatchWorkspaceResponse>> Handle(
    GetDispatchWorkspaceQuery query,
    CancellationToken ct
  )
  {
    var actor = await ExecutionCommandSupport.ActorAsync(db, caller, roles, ct);
    if (actor is null)
      return RequestResponse<DispatchWorkspaceResponse>.Fail(
        "Access denied.",
        403
      );
    var state = await DispatchWorkspaceReader.ReadAsync(
      db,
      query.DispatchId,
      true,
      ct
    );
    if (state is not null)
    {
      await PlaceAsync(state.Response, inputs, ct);
      await deadhead.ReadAsync([state.Response.Load], ct);
      var forecasts = state
        .Legs.Where(x => x.Status is "active" or "planned")
        .Select(x =>
          DispatchProjection.FromExecution(
            ExecutionLoadProjection.Capture(
              state.Load,
              x,
              ExecutionStopRows.Read(x)
            )
          )
        )
        .ToArray();
      if (forecasts.Length == 0)
        await eta.PopulateAsync([state.Response.Load], ct);
      else
      {
        await eta.PopulateAsync(forecasts, ct);
        var snapshots = forecasts
          .Where(x => x.Eta is not null)
          .Select(x => x.Eta!)
          .ToArray();
        if (snapshots.Length > 0)
          state.Response.Load.Eta = new DispatchEta(
            snapshots.Min(x => x.CalculatedAt),
            snapshots.Min(x => x.ValidUntil),
            snapshots
              .SelectMany(x => x.Stops)
              .Where(x => state.Response.Stops.Any(s => s.Id == x.StopId))
              .DistinctBy(x => x.StopId)
              .ToArray(),
            snapshots
              .FirstOrDefault(x => x.UnavailableReason is not null)
              ?.UnavailableReason,
            snapshots.SelectMany(x => x.Assumptions).Distinct().ToArray()
          )
          {
            RouteUpdatePending = snapshots.Any(x => x.RouteUpdatePending),
          };
      }
    }
    return state is null
      ? RequestResponse<DispatchWorkspaceResponse>.Fail("Load not found.", 404)
      : RequestResponse<DispatchWorkspaceResponse>.Ok(state.Response);
  }

  // Each accepted leg, and the load, placed as the board places them: by
  // the planning inputs of the truck the leg is on, read in one cached
  // batch, through the same WorkPlacements. The load's own place is its
  // active (else first open) leg's, or for an older load its truck's.
  internal static async Task PlaceAsync(
    DispatchWorkspaceResponse response,
    TruckPlanningInputsReader inputs,
    CancellationToken ct
  )
  {
    var load = response.Load;
    var open = response
      .AcceptedAssignments.Where(x => x.Status is "active" or "planned")
      .ToArray();
    var trucks = open.Select(x => x.Resources.TruckId)
      .Append(open.Length == 0 ? load.TruckId : null)
      .OfType<Guid>()
      .Distinct()
      .ToArray();
    if (trucks.Length == 0)
      return;
    var work = await inputs.ReadManyAsync(trucks, ct, includeHos: false);
    var placements = trucks.ToDictionary(
      x => x,
      x => work.GetValueOrDefault(x)?.Placements()
    );
    string? Phase(Guid? truck, Guid? leg, long revision) =>
      truck is { } id
        ? WorkPlacements.Phase(
          placements.GetValueOrDefault(id),
          new(load.Id, leg),
          revision
        ) ?? "unknown"
        : null;
    response.AcceptedAssignments =
    [
      .. response.AcceptedAssignments.Select(x =>
      {
        if (x.Status is not ("active" or "planned"))
          return x;
        var phase = Phase(x.Resources.TruckId, x.ExecutionLegId, x.Revision);
        return x with
        {
          Phase = phase,
          Conflict = WorkPlacements.Conflict(
            phase,
            load.Completed,
            load.CargoDelivered
          ),
        };
      }),
    ];
    var lead =
      response.AcceptedAssignments.FirstOrDefault(x => x.Status == "active")
      ?? response.AcceptedAssignments.FirstOrDefault(x => x.Phase is not null);
    load.WorkPhase = lead is not null
      ? lead.Phase
      : Phase(
        load.TruckId,
        null,
        PlanningWorkPolicy.AcceptedRevision(
          null,
          load.AssignmentRevision,
          load.PlanningAssignmentRevision
        )
      );
    load.WorkConflict = WorkPlacements.Conflict(
      load.WorkPhase,
      load.Completed,
      load.CargoDelivered
    );
  }
}
