using Domain.Entities.Dispatch;

namespace Application.Features.Execution.Models;

public static class ExecutionActualChronology
{
  public static bool Ordered(IEnumerable<DispatchStop> stops)
  {
    DateTime? previous = null;
    foreach (var stop in stops)
    {
      var times = new[]
      {
        stop.ArrivedAt,
        stop.PickedUpAt,
        stop.DeliveredAt,
        stop.DepartedAt,
        stop.ManualCompletedAt,
      };
      if (times.Min() < previous)
        return false;
      previous = times.Max() ?? previous;
    }
    return true;
  }
}
