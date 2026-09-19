namespace Domain.Entities.Dispatch;

public static class StopOperation
{
  public static bool Valid(string? action, string? state) =>
    (action, state) switch
    {
      (null, null) => true,
      ("Driver start", "No truck") => true,
      ("Collect truck", "Bobtail" or "Empty" or "Loaded" or "Unknown") => true,
      ("Collect trailer", "Empty" or "Loaded" or "Unknown") => true,
      ("Pick Up", "Loaded") => true,
      ("Drop Off", "Empty" or "Loaded" or "Unknown") => true,
      ("Drop trailer", "Bobtail") => true,
      ("Waypoint", "Bobtail" or "Empty" or "Loaded" or "Unknown") => true,
      _ => false,
    };

  public static List<DispatchStop> Resolve(
    IEnumerable<DispatchStop> source,
    Guid? truckStart
  ) =>
    Resolve(
      source,
      truckStart,
      (stop, job, state) => stop.WithOperation(job, state)
    );

  public static List<T> Resolve<T>(
    IEnumerable<T> source,
    Guid? truckStart,
    Func<T, string, string, T> withOperation
  )
    where T : class, ITruckPathStop
  {
    var state = "Unknown";
    var result = new List<T>();
    foreach (var stop in source.OrderBy(s => s.Sequence))
    {
      if (
        state == "No truck"
        && (
          stop.Id == truckStart
          || stop.TruckId.HasValue
          || !string.IsNullOrWhiteSpace(stop.TruckNumber)
        )
      )
        state = "Unknown";
      var job = stop.ManualAction ?? stop.Job;
      if (stop.ManualStateAfter is { } confirmed)
        state = confirmed;
      else if (state != "No truck")
      {
        if (
          job.Equals("Pick Up", StringComparison.OrdinalIgnoreCase)
          || job.Equals("Pickup", StringComparison.OrdinalIgnoreCase)
        )
          state = "Loaded";
        else if (
          job.Equals("Drop Off", StringComparison.OrdinalIgnoreCase)
          || job.Equals("Delivery", StringComparison.OrdinalIgnoreCase)
        )
          state = "Unknown";
      }
      result.Add(withOperation(stop, job, state));
    }
    return result;
  }
}
