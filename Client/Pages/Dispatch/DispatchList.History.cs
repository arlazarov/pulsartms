using Client.Models.DTO;
using Client.Models.DTO.Dispatch;
using Client.Services;
using Client.Shared.Dispatch;

namespace Client.Pages.Dispatch;

// A search from Cards or Papers also finds completed loads (the owner,
// September 27): those views stay Active-only, so the loads the search
// finds in history are listed under them, labelled, a page of 12 at a time
// with the way to every other page, from the same server search the
// completed Table uses.
public partial class DispatchList
{
  private IReadOnlyList<DispatchResponse>? _history;
  private int _historyTotal;
  private int _historyPage = 1;
  private int _historyPages;
  private bool _historyFailed;
  private CancellationTokenSource? _historyRequest;

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

  // The read of one search's history: a newer search, a cleared one or
  // another view cancels it, and an answer for anything but the list now
  // shown is dropped.
  private void StartHistory(
    DispatchBoardRequest query,
    int version,
    int page = 1
  )
  {
    _historyPage = page;
    _historyRequest?.Cancel();
    _historyRequest = null;
    _historyFailed = false;
    if (!SearchesHistory(query))
    {
      _history = null;
      return;
    }
    var request = CancellationTokenSource.CreateLinkedTokenSource(
      _lifetime.Token
    );
    _historyRequest = request;
    _ = ReadHistoryAsync(query, version, page, request);
  }

  private void RetryHistory()
  {
    if (_loadedQuery is { } query)
      StartHistory(query, _boardVersion, _historyPage);
  }

  private void HistoryPage(int page)
  {
    if (_loadedQuery is { } query && page >= 1 && page <= _historyPages)
      StartHistory(query, _boardVersion, page);
  }

  private async Task ReadHistoryAsync(
    DispatchBoardRequest query,
    int version,
    int page,
    CancellationTokenSource request
  )
  {
    try
    {
      var result = await Api.GetAsync<PaginatedListDTO<DispatchResponse>>(
        (query with { Page = page, Completed = true }).Url,
        request.Token
      );
      if (
        _disposed
        || request.IsCancellationRequested
        || version != _boardVersion
        || query != BoardRequest(_page)
      )
        return;
      // A failed read is said as one, never shown as "nothing found".
      _historyFailed = !result.Success || result.Response is null;
      _history = _historyFailed ? null : result.Response!.Items;
      _historyTotal = result.Response?.TotalCount ?? 0;
      _historyPages = result.Response?.TotalPages ?? 0;
      await InvokeAsync(StateHasChanged);
    }
    finally
    {
      if (ReferenceEquals(_historyRequest, request))
        _historyRequest = null;
      request.Dispose();
    }
  }
}
