using Client.Models.DTO.Dispatch.Workspace;

namespace Client.Pages.Dispatch;

public static class DispatchStopDraftOrder
{
  public static bool CanMove(
    IReadOnlyList<DispatchWorkspaceStop> stops,
    Guid id,
    int target
  )
  {
    var index = Index(stops, id);
    if (
      index < 0
      || target < 0
      || target >= stops.Count
      || target == index
      || !stops[index].CanMove
    )
      return false;
    var segment = stops[index].SegmentKey;
    return stops
      .Skip(Math.Min(index, target))
      .Take(Math.Abs(index - target) + 1)
      .All(stop => stop.CanMove && stop.SegmentKey == segment);
  }

  public static bool Move(
    List<DispatchWorkspaceStop> stops,
    Guid id,
    int target
  )
  {
    if (!CanMove(stops, id, target))
      return false;
    var index = Index(stops, id);
    var stop = stops[index];
    stops.RemoveAt(index);
    stops.Insert(target, stop);
    Renumber(stops);
    return true;
  }

  public static void Renumber(List<DispatchWorkspaceStop> stops)
  {
    for (var index = 0; index < stops.Count; index++)
      stops[index].Sequence = index + 1;
  }

  public static int Index(IReadOnlyList<DispatchWorkspaceStop> stops, Guid id)
  {
    for (var index = 0; index < stops.Count; index++)
      if (stops[index].Id == id)
        return index;
    return -1;
  }
}
