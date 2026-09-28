using Client.Models.DTO.Dispatch;

namespace Client.Shared.Dispatch;

// What a load's stop badge says, in the load's stop order: P for a pickup;
// D for the load's only delivery, D1, D2, ... when it has several. A stop
// that neither loads nor unloads keeps its place in the load. A badge never
// says which load of a chain it belongs to. The Fleet map draws the same
// words (Scripts/fleetMap/routes/stopLabels.ts).
public static class StopMarkers
{
  // Every stop of a truck's trip chain by its number there (the owner,
  // September 28): its place across the chain, in the chain's order and
  // each load's stop order as the trip cards show them - a plain 1, 2, 3,
  // with no letter or icon; the stop's job is in its accessible name and
  // its card. The one owner of these numbers: the map is sent
  // this (FleetMap PushStopBadgesAsync) and the trip cards and stop cards
  // read it, so they never disagree, and choosing a trip, a stop or a
  // layer renumbers nothing. A stop outside the chain owns no number.
  public static IReadOnlyDictionary<Guid, string> ChainBadges(
    IEnumerable<DispatchResponse> loads
  )
  {
    var badges = new Dictionary<Guid, string>();
    var number = 0;
    foreach (var load in loads)
    foreach (var visit in DispatchStopPresentation.OrderedVisits(load.Stops))
    {
      number++;
      if (visit.Stop.Id != Guid.Empty)
        badges.TryAdd(
          visit.Stop.Id,
          number.ToString(System.Globalization.CultureInfo.InvariantCulture)
        );
    }
    return badges;
  }

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
