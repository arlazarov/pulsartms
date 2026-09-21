namespace Domain.Models.Execution;

public enum WorkActivity
{
  ActiveExecution,
  Started,
  Upcoming,
}

// This is the existing selection order, not proof of physical precedence.
public sealed record WorkOrderKey(
  WorkActivity Activity,
  DateTime ScheduledLocalStart,
  int LoadNumber
) : IComparable<WorkOrderKey>
{
  public int CompareTo(WorkOrderKey? other)
  {
    if (other is null)
      return 1;
    var activity = Activity.CompareTo(other.Activity);
    if (activity != 0)
      return activity;
    var schedule = ScheduledLocalStart.CompareTo(other.ScheduledLocalStart);
    return schedule != 0 ? schedule : LoadNumber.CompareTo(other.LoadNumber);
  }
}
