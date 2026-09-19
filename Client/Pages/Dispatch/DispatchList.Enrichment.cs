using System.Net;
using Client.Models.DTO;
using Client.Models.DTO.Dispatch;
using Client.Models.DTO.Fleet;
using Client.Models.DTO.Planning;
using Client.Shared.Dispatch;

namespace Client.Pages.Dispatch;

public partial class DispatchList
{
  private CancellationTokenSource? _enrichmentRequest;
  private bool _enrichmentFailed;
  private Task? _hosPollingTask;
  private Dictionary<Guid, TruckHosSnapshot> _hosSnapshot = [];

  private async Task<bool> RefreshHosAsync(CancellationToken ct)
  {
    if (_disposed || _showCompleted || _data is null)
      return false;
    var version = _boardVersion;
    var ids = _data
      .Items.Where(x => x.TruckId.HasValue)
      .Select(x => x.TruckId!.Value)
      .Distinct()
      .ToArray();
    if (ids.Length == 0)
      return true;
    var result = await Api.GetAsync<Dictionary<Guid, TruckHosSnapshot>>(
      "api/fleet/hos?" + string.Join("&", ids.Select(id => $"truckIds={id}")),
      ct
    );
    if (
      _disposed
      || ct.IsCancellationRequested
      || version != _boardVersion
      || !result.Success
      || result.Response is null
    )
      return false;
    _hosSnapshot = result.Response;
    ApplyHos();
    await InvokeAsync(StateHasChanged);
    return _hosSnapshot.Values.Any(x => x.Hos is not null);
  }

  private void ApplyHos()
  {
    foreach (var row in _data?.Items ?? [])
      if (
        row.TruckId is { } id
        && _hosSnapshot.TryGetValue(id, out var snapshot)
      )
      {
        if (row.DriverName != snapshot.DriverName)
          row.CurrentCycle = null;
        row.DriverName = snapshot.DriverName;
        row.Hos = snapshot.Hos;
      }
  }

  private async Task EnrichBoardAsync(
    DispatchBoardRequest query,
    int version,
    CancellationToken ct
  )
  {
    if (_data is null || !_data.Items.Any(row => row.Dispatches.Count > 0))
      return;
    _enrichmentRequest?.Cancel();
    using var request = CancellationTokenSource.CreateLinkedTokenSource(ct);
    _enrichmentRequest = request;
    _enrichmentFailed = false;
    try
    {
      await Task.WhenAll(
        ReadEnrichmentAsync(query, version, false, request.Token),
        ReadEnrichmentAsync(query, version, true, request.Token)
      );
    }
    catch (OperationCanceledException) when (request.IsCancellationRequested)
    { }
    finally
    {
      if (ReferenceEquals(_enrichmentRequest, request))
      {
        _enrichmentRequest = null;
        if (!_disposed)
          await InvokeAsync(StateHasChanged);
      }
    }
  }

  private async Task ReadEnrichmentAsync(
    DispatchBoardRequest query,
    int version,
    bool financials,
    CancellationToken ct
  )
  {
    var result = await Api.GetAsync<List<TruckDispatchEnrichment>>(
      query.Url.Replace("board?", "board/enrichment?", StringComparison.Ordinal)
        + $"&includeHos=false&includeFinancials={financials.ToString().ToLowerInvariant()}&includeEta={(!financials).ToString().ToLowerInvariant()}",
      ct
    );
    if (
      _disposed
      || ct.IsCancellationRequested
      || version != _boardVersion
      || query != BoardRequest(_page)
    )
      return;
    if (!result.Success || result.Response is null)
    {
      _enrichmentFailed = true;
      if (
        result.HttpStatusCode
        is HttpStatusCode.Unauthorized
          or HttpStatusCode.Forbidden
      )
      {
        _data = null;
        _error = result.ErrorMessage;
      }
      return;
    }
    var byKey = result.Response.ToDictionary(
      row => row.Key,
      StringComparer.Ordinal
    );
    foreach (var row in _data?.Items ?? [])
    {
      if (
        !byKey.TryGetValue(row.Key, out var incoming)
        || incoming.TruckId != row.TruckId
        || row.Dispatches.Count != incoming.Dispatches.Count
        || !row
          .Dispatches.Zip(incoming.Dispatches)
          .All(pair => SameInputs(pair.First, pair.Second))
      )
        continue;
      if (!financials && row.DriverName != incoming.DriverName)
        continue;
      if (!financials)
        row.CurrentCycle = incoming.CurrentCycle;
      foreach (var (load, enriched) in row.Dispatches.Zip(incoming.Dispatches))
      {
        if (financials && enriched.Financials is { } values)
        {
          load.EmptyMiles = values.EmptyMiles;
          load.TotalMiles = values.TotalMiles;
          load.LoadedRatePerMile = values.LoadedRatePerMile;
          load.TotalRatePerMile = values.TotalRatePerMile;
          load.EmptyMilesStatus = values.EmptyMilesStatus;
        }
        else if (
          enriched.Eta is { } forecast
          && (
            load.Eta is null
            || forecast.Stops.Count == 0
            || forecast.CalculatedAt >= load.Eta.CalculatedAt
          )
        )
          load.Eta = forecast;
      }
    }
    await InvokeAsync(StateHasChanged);
  }

  private static bool SameInputs(
    DispatchResponse left,
    DispatchResponse right
  ) =>
    left.Id == right.Id
    && left.TruckId == right.TruckId
    && left.ExecutionLegId == right.ExecutionLegId
    && left.AssignmentRevision == right.AssignmentRevision
    && left.LastSyncedAt == right.LastSyncedAt
    && left.PlanningAssignmentRevision == right.PlanningAssignmentRevision
    && left.RouteChoiceRevision == right.RouteChoiceRevision
    && left.Stops.Count == right.Stops.Count
    && left.Stops.Zip(right.Stops)
      .All(pair =>
        pair.First.Id == pair.Second.Id
        && pair.First.ManualCompletionRevision
          == pair.Second.ManualCompletionRevision
        && pair.First.OperationRevision == pair.Second.OperationRevision
      );

  private static bool SameInputs(
    DispatchResponse left,
    DispatchEnrichment right
  ) =>
    left.Id == right.Id
    && left.TruckId == right.TruckId
    && left.ExecutionLegId == right.ExecutionLegId
    && left.AssignmentRevision == right.AssignmentRevision
    && left.LastSyncedAt == right.LastSyncedAt
    && left.PlanningAssignmentRevision == right.PlanningAssignmentRevision
    && left.RouteChoiceRevision == right.RouteChoiceRevision
    && left.Stops.Count == right.Stops.Count
    && left.Stops.Zip(right.Stops)
      .All(pair =>
        pair.First.Id == pair.Second.Id
        && pair.First.ManualCompletionRevision
          == pair.Second.ManualCompletionRevision
        && pair.First.OperationRevision == pair.Second.OperationRevision
      );

  private static (Guid, Guid?, long) LoadIdentity(DispatchResponse load) =>
    (load.Id, load.ExecutionLegId, load.AssignmentRevision);

  private static object PlanningIdentity(TruckDispatchBoardResponse truck) =>
    (
      truck.Key,
      LoadIdentity(truck.Dispatches[0]),
      DispatchStopPresentation.CompletionRevision(truck.Dispatches[0].Stops)
    );

  private static void CopyFinancials(
    DispatchResponse source,
    DispatchResponse target
  )
  {
    target.EmptyMiles = source.EmptyMiles;
    target.TotalMiles = source.TotalMiles;
    target.LoadedRatePerMile = source.LoadedRatePerMile;
    target.TotalRatePerMile = source.TotalRatePerMile;
    target.EmptyMilesStatus = source.EmptyMilesStatus;
  }
}
