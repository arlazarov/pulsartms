using Application.Features.Dispatch.Models;
using Application.Features.Dispatch.Services;
using Application.Features.Eta.Models;
using Application.Features.Eta.Services;
using Application.Features.Execution.Models;
using Application.Features.Execution.Services;
using Application.Features.Routing.Services.Deadheads;
using Application.Models;

namespace Application.Features.Dispatch.Queries;

public sealed record GetDispatchWorkspaceQuery(Guid DispatchId)
  : IRequest<RequestResponse<DispatchWorkspaceResponse>>;

public sealed class GetDispatchWorkspaceHandler(
  IAppDbContext db,
  ICurrentUser caller,
  IUserRoleService roles,
  EtaForecastService eta,
  DeadheadService deadhead
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
}
