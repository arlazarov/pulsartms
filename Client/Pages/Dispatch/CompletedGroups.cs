using Client.Models.DTO.Dispatch;

namespace Client.Pages.Dispatch;

// Completed loads arrive a page at a time, newest load number first, one
// row per load. They are shown a truck at a time: each truck once, in the
// order of its newest load on the page, with its loads beneath it newest
// first. Only this page is grouped - a truck's other completed loads may
// be on other pages. A load keeps the driver and trailer it had; the
// truck's heading names one only when all its loads here share it.
public static class CompletedGroups
{
  public static List<TruckDispatchBoardResponse> Group(
    IEnumerable<DispatchResponse> loads
  ) =>
    [
      .. loads
        .GroupBy(x => x.TruckId?.ToString() ?? $"unit:{x.TruckNumber.Trim()}")
        .Select(group =>
        {
          var list = group.ToList();
          return new TruckDispatchBoardResponse
          {
            Key = $"completed:{group.Key}",
            TruckId = list[0].TruckId,
            TruckNumber = list[0].TruckNumber,
            DriverName = Shared(list.Select(x => x.DriverName)),
            TrailerNumber = Shared(list.Select(x => x.TrailerNumber)),
            Dispatches = list,
          };
        }),
    ];

  private static string Shared(IEnumerable<string> values)
  {
    var distinct = values.Select(x => x.Trim()).Distinct().ToList();
    return distinct.Count == 1 ? distinct[0] : "";
  }
}
