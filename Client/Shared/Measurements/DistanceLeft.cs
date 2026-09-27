namespace Client.Shared.Measurements;

// What a truck's head says is left: the miles to the stop the truck is
// heading for, because that is the stop its ETA is for. It was once the
// remainder of the whole run, so a truck on its way to a pickup read the
// miles to its delivery next to the hour of its pickup. Only where no next
// stop is known is the run's remainder said instead. Fleet Map and Dispatch
// both say it this way.
public static class DistanceLeft
{
  public static double? Miles(double? toNextStop, double? toRunEnd) =>
    toNextStop ?? toRunEnd;
}
