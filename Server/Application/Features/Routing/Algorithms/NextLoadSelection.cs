using DispatchEntity = Domain.Entities.Dispatch.Dispatch;

namespace Application.Features.Routing.Algorithms;

public static class NextLoadSelection
{
  public static List<DispatchEntity> Select(IEnumerable<DispatchEntity> source, Guid? currentId)
  {
    var loads = source.Where(x => x.Status is "assigned" or "in_transit")
      .OrderBy(x => x.Status == "in_transit" ? 0 : 1)
      .ThenBy(x => x.Stops.OrderBy(s => s.Sequence).FirstOrDefault()?.ScheduledDate ?? DateOnly.MaxValue)
      .ThenBy(x => x.Stops.OrderBy(s => s.Sequence).FirstOrDefault()?.ScheduledTime ?? TimeOnly.MinValue)
      .ThenBy(x => x.Id).ToList();
    var current = loads.FirstOrDefault(x => x.Id == currentId) ?? loads.FirstOrDefault();
    return loads.SkipWhile(x => x.Id != current?.Id).Skip(1).Where(x => x.Status == "assigned").ToList();
  }
}
