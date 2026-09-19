namespace Application.Features.Eta.Algorithms;

public static class StopServicePolicy
{
  public static bool IsEquipmentOperation(string job) =>
    job
      is "Collect truck"
        or "Collect trailer"
        or "Drop trailer"
        or "Waypoint"
        or "Driver start";

  public static int Minutes(
    string job,
    int pickupMinutes,
    int deliveryMinutes
  ) =>
    job.Equals("Pick Up", StringComparison.OrdinalIgnoreCase)
    || job.Equals("Pickup", StringComparison.OrdinalIgnoreCase)
      ? pickupMinutes
    : job.Equals("Drop Off", StringComparison.OrdinalIgnoreCase)
    || job.Equals("Delivery", StringComparison.OrdinalIgnoreCase)
      ? deliveryMinutes
    : IsEquipmentOperation(job) ? 0
    : 60;

  public static void WaitUntil(
    HosTravelClock clock,
    DateTimeOffset start,
    string job
  )
  {
    if (IsEquipmentOperation(job))
      clock.Service(Math.Max(0, (start - clock.Now).TotalHours));
    else
      clock.WaitUntil(start);
  }
}
