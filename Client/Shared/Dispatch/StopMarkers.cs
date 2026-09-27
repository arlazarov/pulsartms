namespace Client.Shared.Dispatch;

// What a load's stop badge says, in the load's stop order: P for a pickup;
// D for the load's only delivery, D1, D2, ... when it has several. A stop
// that neither loads nor unloads keeps its place in the load. A badge never
// says which load of a chain it belongs to. The Fleet map draws the same
// words (Scripts/fleetMap/routes/stopLabels.ts).
public static class StopMarkers
{
  public static IReadOnlyList<string> Labels(IReadOnlyList<string?> jobs)
  {
    var deliveries = jobs.Count(DispatchStopPresentation.IsDeliveryJob);
    var delivery = 0;
    return jobs.Select(
        (job, index) =>
          DispatchStopPresentation.IsPickupJob(job) ? "P"
          : DispatchStopPresentation.IsDeliveryJob(job)
            ? deliveries == 1 ? "D"
              : $"D{++delivery}"
          : (index + 1).ToString(
            System.Globalization.CultureInfo.InvariantCulture
          )
      )
      .ToArray();
  }
}
