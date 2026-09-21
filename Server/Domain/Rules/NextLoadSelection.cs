using Domain.Entities.Execution;

namespace Domain.Rules;

// Which of a truck's loads are the work ahead of it, in the order it will
// do them: the one being driven first, then by when each begins.
public static class NextLoadSelection
{
  public static List<T> Select<T>(
    IEnumerable<T> source,
    Guid? currentId,
    Guid? currentExecutionLegId = null
  )
    where T : class, IWorkFacts
  {
    var loads = source
      .Where(x =>
        x.ExecutionLegId.HasValue
          ? x.ExecutionStatus is "active" or "planned"
          : x.Status is "assigned" or "in_transit"
      )
      .Where(x =>
        !x.Stops.Any(s =>
          s.ManualCompletedAt.HasValue || s.CompletionOverride == true
        ) || !x.Stops.All(s => s.IsCompleted)
      )
      .OrderBy(x =>
        x.ExecutionLegId.HasValue
          ? x.ExecutionStatus == "active"
            ? 0
            : 1
          : x.Status == "in_transit"
            ? 0
            : 1
      )
      .ThenBy(x =>
        x.Stops.OrderBy(s => s.Sequence).FirstOrDefault()?.ScheduledDate
        ?? DateOnly.MaxValue
      )
      .ThenBy(x =>
        x.Stops.OrderBy(s => s.Sequence).FirstOrDefault()?.ScheduledTime
        ?? TimeOnly.MinValue
      )
      .ThenBy(x => x.Id)
      .ThenBy(x => x.ExecutionLegId)
      .ToList();
    var current =
      loads.FirstOrDefault(x =>
        x.Id == currentId && x.ExecutionLegId == currentExecutionLegId
      ) ?? loads.FirstOrDefault();
    if (current is null)
      return [];
    return loads
      .SkipWhile(x =>
        x.Id != current.Id || x.ExecutionLegId != current.ExecutionLegId
      )
      .Skip(1)
      .ToList();
  }
}
