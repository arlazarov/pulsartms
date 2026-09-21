using Application.Features.Execution.Services;
using Application.Models;
using Domain.Models.Execution;

namespace Application.Features.Execution.Queries;

public sealed record PreviewSwitchQuery(PlanSwitchRequest Request)
  : IRequest<RequestResponse<SwitchPreview>>;

public sealed class PreviewSwitchHandler(
  IAppDbContext db,
  ICurrentUser caller,
  IUserRoleService roles
) : IRequestHandler<PreviewSwitchQuery, RequestResponse<SwitchPreview>>
{
  public async Task<RequestResponse<SwitchPreview>> Handle(
    PreviewSwitchQuery query,
    CancellationToken ct
  )
  {
    if (await ExecutionCommandSupport.ActorAsync(db, caller, roles, ct) is null)
      return RequestResponse<SwitchPreview>.Fail("Access denied.", 403);
    var request = query.Request;
    if (!SwitchPlanningRules.Valid(request))
      return Result(
        [
          "Choose the transfer location, exact boundary visits, "
            + "distinct resources and valid release/receive times.",
        ],
        []
      );
    var ids = request.Loads.Select(x => x.DispatchId).ToArray();
    var loads = await db
      .Dispatches.AsNoTracking()
      .Include(x => x.Stops)
      .Where(x => ids.Contains(x.Id))
      .ToDictionaryAsync(x => x.Id, ct);
    var problems = new List<string>();
    var previews = new List<SwitchPreviewLoad>();
    if (
      !await ExecutionResources.ActiveAsync(
        db,
        request
          .Loads.SelectMany(x => new[] { x.Outgoing, x.Incoming })
          .ToArray(),
        ct
      )
    )
      problems.Add("Select active trucks, drivers and trailers.");
    foreach (var item in request.Loads)
    {
      if (!loads.TryGetValue(item.DispatchId, out var load))
      {
        problems.Add("A selected load is no longer available.");
        continue;
      }
      var split = SwitchPlanningRules.Split(
        load,
        item,
        request.ConfirmCompleted
      );
      if (split is null)
      {
        problems.Add(
          $"Load {load.LoadNumber}: the source or boundary visits changed."
        );
        continue;
      }
      if (item.OutgoingLegId is { } legId)
      {
        var leg = await db
          .ExecutionLegs.AsNoTracking()
          .Include(x => x.Loads)
          .SingleOrDefaultAsync(x => x.Id == legId, ct);
        if (
          leg is null
          || leg.Status is not ("active" or "planned")
          || leg.SourceReviewReason is not null
          || leg.EndSwitchId.HasValue
          || leg.Loads.Count != 1
          || leg.Loads[0].DispatchId != load.Id
          || leg.Revision != item.ExpectedOutgoingRevision
          || ExecutionCommandSupport.Assignment(leg) != item.Outgoing
        )
        {
          problems.Add(
            $"Load {load.LoadNumber}: the outgoing assignment changed "
              + "or already has a planned switch."
          );
          continue;
        }
        var retained = ExecutionStopRows.Read(leg);
        var at = retained.FindIndex(x => x.Id == split.Value.Before[^1].Id);
        if (at < 0 || retained.Skip(at + 1).Any(x => x.IsCompleted))
        {
          problems.Add(
            $"Load {load.LoadNumber}: recorded execution "
              + "must be reconciled first."
          );
          continue;
        }
        split = (retained.Take(at + 1).ToList(), split.Value.After);
      }
      else if (
        await SwitchSourceAssignment.StatusAsync(
          db,
          load,
          item,
          split.Value.Before,
          request.ConfirmCompleted,
          ct
        )
        is null
      )
      {
        problems.Add(
          $"Load {load.LoadNumber}: select its confirmed outgoing assignment; "
            + "the source history or assigned resources conflict."
        );
        continue;
      }
      var trips = new[] { item.OutgoingTripId, item.IncomingTripId }
        .Where(x => x.HasValue)
        .Select(x => x!.Value)
        .Distinct()
        .ToArray();
      if (
        request.ConfirmCompleted
        && await ExecutionResources.ConflictsAsync(db, item.Incoming, null, ct)
      )
        problems.Add("Incoming resources are assigned elsewhere.");
      if (
        await db.Trips.CountAsync(
          x =>
            trips.Contains(x.Id)
            && (x.Status == "active" || x.Status == "planned"),
          ct
        ) != trips.Length
      )
        problems.Add(
          $"Load {load.LoadNumber}: a selected trip is no longer available."
        );
      previews.Add(
        new(
          load.Id,
          item.Outgoing,
          item.Incoming,
          split.Value.Before.Select(SwitchReads.Visit).ToArray(),
          split.Value.After.Select(SwitchReads.Visit).ToArray()
        )
      );
    }
    return Result(problems, previews);
  }

  private static RequestResponse<SwitchPreview> Result(
    IReadOnlyList<string> problems,
    IReadOnlyList<SwitchPreviewLoad> loads
  ) =>
    RequestResponse<SwitchPreview>.Ok(
      new(problems.Count == 0, problems, loads)
    );
}
