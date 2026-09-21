using Application.Features.Execution.Interfaces;
using Application.Features.Routing.Interfaces;
using Domain.Models.Execution;
using Domain.Models.Routing;
using Domain.Rules;

namespace Application.Features.Routing.Services.Routes;

public sealed class SavedRoadValidation(
  IAppDbContext db,
  INextLoadRouteReader roads,
  ISavedRoutePlanReader plans,
  IExecutionReadScope scope
) : ISavedRoadValidation
{
  public async Task RequireCurrentAsync(
    IReadOnlyCollection<SavedRoadVersion> expected,
    CancellationToken ct
  )
  {
    if (db.Database.CurrentTransaction is null)
      throw new InvalidOperationException(
        "Saved-road validation requires a publication transaction."
      );
    if (!await CompareAsync(expected, true, ct))
      throw new RoutePlanningException(
        "Saved roads changed. Recalculate the plan."
      );
  }

  public Task<bool> MatchesAsync(
    IReadOnlyCollection<SavedRoadVersion> expected,
    CancellationToken ct
  ) => scope.ReadAsync(token => CompareAsync(expected, false, token), ct);

  private async Task<bool> CompareAsync(
    IReadOnlyCollection<SavedRoadVersion> expected,
    bool includeProgress,
    CancellationToken ct
  )
  {
    ct.ThrowIfCancellationRequested();
    var versions = new Dictionary<WorkIdentity, NextLoadRouteVersion>();
    var metadata = new Dictionary<WorkIdentity, SavedRoutePlanMetadata>();
    foreach (var native in new[] { false, true })
    {
      var selected = expected
        .Where(x => x.Work.ExecutionLegId.HasValue == native)
        .ToArray();
      var roadIds = selected
        .Where(x => x.Kind != SavedRoadKind.Plan)
        .Select(x => x.Work.ExecutionLegId ?? x.Work.DispatchId)
        .Distinct()
        .ToArray();
      if (roadIds.Length > 0)
        foreach (
          var row in native
            ? await roads.ReadExecutionVersionsAsync(roadIds, ct)
            : await roads.ReadVersionsAsync(roadIds, ct)
        )
          versions[new(row.DispatchId, row.ExecutionLegId)] = row;
      var roots = selected
        .Where(x => x.Kind == SavedRoadKind.Plan)
        .Select(x => x.Work)
        .Distinct()
        .ToArray();
      if (roots.Length == 0)
        continue;
      var planIds = roots
        .Select(x => x.ExecutionLegId ?? x.DispatchId)
        .ToArray();
      var saved = native
        ? await plans.ReadExecutionLegsAsync(planIds, ct)
        : await plans.ReadManyAsync(planIds, ct);
      foreach (var work in roots)
        if (
          saved.TryGetValue(work.ExecutionLegId ?? work.DispatchId, out var row)
        )
          metadata[work] = row;
    }
    // Preserve every observation, including repeated reads of the same source.
    foreach (var capture in expected)
    {
      var row =
        versions.GetValueOrDefault(capture.Work)
        ?? NextLoadRouteVersion.From(
          capture.Work.DispatchId,
          capture.Work.ExecutionLegId,
          null
        );
      var current = capture.Kind switch
      {
        SavedRoadKind.Base => SavedRoadVersion.Base(row),
        SavedRoadKind.Connection => SavedRoadVersion.Connection(row),
        SavedRoadKind.Plan => SavedRoadVersion.Plan(
          capture.Work,
          metadata.GetValueOrDefault(capture.Work)
        ),
        _ => throw new InvalidOperationException("Unknown saved-road kind."),
      };
      if (
        current.Signature != capture.Signature
        || includeProgress
          && current.ProgressSignature != capture.ProgressSignature
      )
        return false;
    }
    return true;
  }
}
