using Client.Models.DTO;
using Client.Models.DTO.Dispatch;
using Client.Services;
using Client.Shared.Dispatch;

namespace Client.Pages.Dispatch;

// A search from Cards or Papers also finds completed loads (the owner,
// September 27): those views stay Active-only, so the loads the search
// finds in history are listed under them, labelled, one page at most, from
// the same server search the completed Table uses.
public partial class DispatchList
{
  private IReadOnlyList<DispatchResponse>? _history;
  private int _historyTotal;

  // A completed load opens like any other, with the way back to this list.
  private string HistoryUrl(DispatchResponse load) =>
    ReturnNavigation.Load(load.Id, ReturnOrigin);

  // Truck, driver and the route, as the completed Table reads them.
  private static string HistoryLine(DispatchResponse load)
  {
    var stops = load.Stops.OrderBy(x => x.Sequence).ToArray();
    var from = stops.FirstOrDefault(x => !x.DriverOnly);
    var to = stops.LastOrDefault();
    return string.Join(
      " · ",
      new[]
      {
        string.IsNullOrWhiteSpace(load.TruckNumber)
          ? ""
          : $"Truck {load.TruckNumber}",
        load.DriverName,
        from is null || to is null
          ? ""
          : $"{DispatchBoardRow.Location(from)} → {DispatchBoardRow.Location(to)}",
        load.CustomerName,
      }.Where(x => !string.IsNullOrWhiteSpace(x))
    );
  }

  private static bool SearchesHistory(DispatchBoardRequest query) =>
    !query.Completed
    && query.View != 1
    && !string.IsNullOrWhiteSpace(query.Search);

  private async Task ReadHistoryAsync(
    DispatchBoardRequest query,
    int version,
    CancellationToken ct
  )
  {
    try
    {
      var result = await Api.GetAsync<PaginatedListDTO<DispatchResponse>>(
        (query with { Page = 1, Completed = true }).Url,
        ct
      );
      if (
        _disposed
        || ct.IsCancellationRequested
        || version != _boardVersion
        || query != BoardRequest(_page)
      )
        return;
      _history = result.Success ? result.Response?.Items : null;
      _historyTotal = result.Response?.TotalCount ?? 0;
      await InvokeAsync(StateHasChanged);
    }
    catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
  }
}
