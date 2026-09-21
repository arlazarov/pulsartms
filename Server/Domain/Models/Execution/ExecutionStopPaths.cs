using Domain.Entities.Dispatch;

namespace Domain.Models.Execution;

public sealed class ExecutionStopPaths
{
  private readonly Guid[] before;
  private readonly Guid[] after;
  private readonly HashSet<Guid> changedPlaces;

  private ExecutionStopPaths(
    IReadOnlyList<DispatchStop> before,
    IReadOnlyList<DispatchStop> after
  )
  {
    this.before = before.Select(x => x.Id).ToArray();
    this.after = after.Select(x => x.Id).ToArray();
    var remaining = after.ToDictionary(x => x.Id);
    changedPlaces = before
      .Where(x =>
        !remaining.TryGetValue(x.Id, out var current) || !SamePlace(x, current)
      )
      .Select(x => x.Id)
      .ToHashSet();
    HasChanges =
      changedPlaces.Count > 0 || !this.before.SequenceEqual(this.after);
  }

  public bool HasChanges { get; }

  public static ExecutionStopPaths Compare(
    IReadOnlyList<DispatchStop> before,
    IReadOnlyList<DispatchStop> after
  ) => new(before, after);

  public bool Affects(Guid? from, Guid? to)
  {
    if (!HasChanges)
      return false;
    var start = Array.IndexOf(before, from ?? Guid.Empty);
    var end = Array.IndexOf(before, to ?? Guid.Empty);
    // Unanchored evidence cannot establish which part of a changed leg it used.
    if (start < 0 && end < 0)
      return true;
    var nextStart = start < 0 ? 0 : Array.IndexOf(after, before[start]);
    var nextEnd =
      end < 0 ? after.Length - 1 : Array.IndexOf(after, before[end]);
    start = start < 0 ? 0 : start;
    end = end < 0 ? before.Length - 1 : end;
    if (nextStart < 0 || nextEnd < 0 || start > end || nextStart > nextEnd)
      return true;
    var length = end - start + 1;
    if (length != nextEnd - nextStart + 1)
      return true;
    for (var i = 0; i < length; i++)
      if (
        before[start + i] != after[nextStart + i]
        || changedPlaces.Contains(before[start + i])
      )
        return true;
    return false;
  }

  private static bool SamePlace(DispatchStop before, DispatchStop after) =>
    before.Job == after.Job
    && before.StateAfter == after.StateAfter
    && before.Name == after.Name
    && before.Address == after.Address
    && before.City == after.City
    && before.Province == after.Province
    && before.Country == after.Country
    && before.ZipCode == after.ZipCode
    && before.Latitude == after.Latitude
    && before.Longitude == after.Longitude;
}
