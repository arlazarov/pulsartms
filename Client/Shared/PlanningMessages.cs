using Client.Models.DTO.Planning;

namespace Client.Shared;

// Where a load's own screen shows a plan's notices, it shows the whole of
// them, each named by its load, after the message.
public static class PlanningMessages
{
  public static string? WithNotices(
    string? message,
    IEnumerable<PlanningNotice>? notices
  )
  {
    var parts = new List<string>();
    if (!string.IsNullOrWhiteSpace(message))
      parts.Add(message);
    foreach (var notice in notices ?? [])
      parts.Add(
        notice.LoadNumber is { } number
          ? $"Load {number}: {notice.Text}"
          : notice.Text
      );
    return parts.Count == 0 ? null : string.Join(" ", parts);
  }
}
