using Application.Features.Execution.Models;
using Domain.Entities.Dispatch;
using Domain.Entities.Execution;
using Domain.Models.Execution;

namespace Application.Features.Execution.Services;

public static class ExecutionImportAcceptance
{
  public static async Task<IReadOnlyList<ExecutionLeg>> ApplyAsync(
    IAppDbContext db,
    IReadOnlyCollection<DispatchSourceLink> sources,
    DateTime now,
    CancellationToken ct
  )
  {
    if (db.Database.CurrentTransaction is null)
      throw new InvalidOperationException(
        "Import acceptance requires its transaction."
      );
    var ids = sources.Select(x => x.DispatchId).ToArray();
    var owned = (
      await db
        .LoadExecutionLegs.Where(x => ids.Contains(x.DispatchId))
        .Select(x => x.DispatchId)
        .Distinct()
        .ToListAsync(ct)
    ).ToHashSet();
    var candidates = new List<Candidate>();
    foreach (var link in sources.Where(x => !owned.Contains(x.DispatchId)))
    {
      var load = link.Dispatch;
      if (load.Status is "cancelled" or "canceled")
      {
        link.ExecutionReviewReason = null;
        continue;
      }
      var stops = StopOperation
        .Resolve(load.Stops, load.PlanningFromStopId)
        .Select(ExecutionSnapshots.Copy)
        .ToArray();
      foreach (
        var stop in stops.Where(x =>
          x.TruckId is null && string.IsNullOrWhiteSpace(x.TruckNumber)
        )
      )
      {
        stop.TruckId = load.TruckId;
        stop.TruckNumber = load.TruckNumber;
      }
      var inputs = InitialExecutionInputs.Resolve(load, stops, now);
      string? review = inputs.ReviewReason;
      if (review is null)
      {
        var first = inputs.Stops[0];
        if (
          load.PlanningAssignmentRevision > 0
          || load.Status
            is not ("assigned" or "planned" or "in_transit" or "completed")
          || stops.Any(x =>
            x.Job
              is not (
                "Pick Up"
                or "Pickup"
                or "Delivery"
                or "Drop Off"
                or "Waypoint"
              )
          )
          || stops.Select(x => x.Sequence).Distinct().Count() != stops.Length
          || load.TruckId.HasValue && load.TruckId != first.TruckId
          || load.DriverId.HasValue && load.DriverId != first.DriverId
          || load.TrailerId.HasValue && load.TrailerId != first.TrailerId
          || load.TruckId is null
            && !string.IsNullOrWhiteSpace(load.TruckNumber)
          || load.DriverId is null
            && !string.IsNullOrWhiteSpace(load.DriverName)
          || load.TrailerId is null
            && !string.IsNullOrWhiteSpace(load.TrailerNumber)
        )
          review =
            "Review source assignments and execution boundaries before accepting this load.";
        else
        {
          var assignment = new ExecutionAssignment(
            first.TruckId!.Value,
            first.DriverId,
            first.TrailerId,
            first.CoDriverId
          );
          var prospective = new ExecutionLeg();
          ExecutionLegProgress.Apply(prospective, inputs.Stops);
          if (!await ExecutionResources.ActiveAsync(db, [assignment], ct))
            review =
              "Source resources are inactive or unresolved. Review the assignment.";
          else if (
            load.Status == "completed" && prospective.Status != "completed"
            || load.Status == "in_transit" && prospective.Status == "planned"
          )
            review =
              "Source status lacks supporting visit facts. Review actual execution.";
          else
            candidates.Add(new(link, inputs, assignment, prospective.Status));
        }
      }
      link.ExecutionReviewReason = review;
    }
    var result = new List<ExecutionLeg>();
    foreach (var candidate in candidates)
    {
      if (
        candidate.Status == "active"
        && (
          candidates.Any(x =>
            x != candidate
            && x.Status == "active"
            && ExecutionResources.Overlap(x.Assignment, candidate.Assignment)
          )
          || await ExecutionResources.ConflictsAsync(
            db,
            candidate.Assignment,
            null,
            ct
          )
        )
      )
      {
        candidate.Source.ExecutionReviewReason =
          "Source work conflicts with another active assignment or transfer. Review the resource boundaries.";
        continue;
      }
      var accepted = await InitialExecutionAssignment.AcceptAsync(
        db,
        candidate.Source.Dispatch,
        candidate.Inputs.Stops,
        null,
        null,
        now,
        ct,
        candidate.Source.AssignmentSignature
      );
      candidate.Source.ExecutionReviewReason = accepted.ReviewReason;
      if (accepted.Leg is { } leg)
        result.Add(leg);
    }
    return result;
  }

  private sealed record Candidate(
    DispatchSourceLink Source,
    InitialExecutionInputs Inputs,
    ExecutionAssignment Assignment,
    string Status
  );
}
