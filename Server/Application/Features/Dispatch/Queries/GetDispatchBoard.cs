using System.Diagnostics;
using Application.Caching;
using Application.Diagnostics;
using Application.Features.Dispatch.Models;
using Application.Features.Eta.Services;
using Application.Features.Execution.Queries;
using Application.Features.Execution.Services;
using Application.Features.Fleet.Interfaces;
using Application.Features.Routing.Services.Deadheads;
using Application.Models;
using Application.Reference;
using Microsoft.Extensions.Logging;

namespace Application.Features.Dispatch.Queries;

public record GetDispatchBoardQuery(
  int Page = 1,
  int PageSize = 12,
  string? Search = null,
  Guid? TruckId = null,
  DateOnly? Date = null,
  bool IncludeHos = true,
  bool IncludePlanned = false,
  bool IncludeFinancials = true,
  bool IncludeEta = true,
  bool IncludeOverdue = false,
  bool IdentitiesOnly = false
)
  : IRequest<RequestResponse<PaginatedList<TruckDispatchBoardResponse>>>,
    IChecked
{
  public IEnumerable<string> Wrong()
  {
    if (Page is < 1 or > 1000000)
      yield return "Choose a page from 1 to 1000000.";
    if (PageSize is < 1 or > 100)
      yield return "Ask for between 1 and 100 rows at a time.";
    if (Search?.Length > 200)
      yield return "That search is too long.";
  }
}

public class GetDispatchBoardHandler(
  IAppDbContext dbContext,
  ReadCache reads,
  IDriverHosProvider hos,
  DeadheadService deadhead,
  EtaForecastService eta,
  FleetNames names,
  ActiveTransfers transfers,
  ILogger<GetDispatchBoardHandler> logger
)
  : IRequestHandler<
    GetDispatchBoardQuery,
    RequestResponse<PaginatedList<TruckDispatchBoardResponse>>
  >
{
  public async Task<
    RequestResponse<PaginatedList<TruckDispatchBoardResponse>>
  > Handle(GetDispatchBoardQuery request, CancellationToken cancellationToken)
  {
    var date = request.Date ?? DateOnly.FromDateTime(DateTime.UtcNow);
    async Task<DispatchBoardIndex> LoadIndex() =>
      new(
        (
          await ExecutionWorkReader.ReadAsync(
            dbContext,
            date,
            names,
            transfers,
            request.TruckId,
            request.IncludePlanned,
            request.IncludeOverdue,
            cancellationToken
          )
        ).Select(DispatchWorkProjection.ToBoardRow)
      );
    var stage = Stopwatch.GetTimestamp();
    var index = await reads.GetAsync(
      "board",
      $"index:{date:O}:{request.TruckId}:{request.IncludePlanned}:{request.IncludeOverdue}",
      LoadIndex
    );
    var indexMs = Take("index", ref stage);
    var result = index.SelectPage(
      request.Page,
      request.PageSize,
      request.Search,
      request.TruckId
    );
    if (request.IdentitiesOnly)
      return RequestResponse<PaginatedList<TruckDispatchBoardResponse>>.Ok(
        result
      );
    var page = result.Items;
    var loadIds = page.SelectMany(x => x.Dispatches)
      .Select(x => x.Id)
      .Distinct()
      .ToArray();
    var legIds = page.SelectMany(x => x.Dispatches)
      .Where(x => x.ExecutionLegId.HasValue)
      .Select(x => x.ExecutionLegId!.Value)
      .Distinct()
      .ToArray();
    var native =
      legIds.Length == 0
        ? new TruckExecutionLoads([], new HashSet<Guid>())
        : await ExecutionLoads.ReadAsync(
          dbContext,
          names,
          transfers,
          request.TruckId,
          loadIds,
          cancellationToken,
          legIds
        );
    var executionMs = Take("execution", ref stage);
    var sourceIds = loadIds
      .Where(id => !native.OwnedDispatchIds.Contains(id))
      .ToArray();
    var details =
      sourceIds.Length == 0
        ? new Dictionary<Guid, DispatchResponse>()
        : await dbContext
          .Dispatches.AsNoTracking()
          .Where(x =>
            sourceIds.Contains(x.Id)
            && !dbContext.LoadExecutionLegs.Any(link => link.DispatchId == x.Id)
          )
          .Select(DispatchProjection.Details)
          .ToDictionaryAsync(x => x.Id, cancellationToken);
    foreach (var detail in details.Values)
      DispatchProjection.Complete(detail);
    var detailsMs = Take("details", ref stage);
    if (request.IncludeFinancials)
      await deadhead.ReadAsync(
        details
          .Values.Where(x => !native.OwnedDispatchIds.Contains(x.Id))
          .ToArray(),
        cancellationToken
      );
    var financialsMs = Take("financials", ref stage);
    var scopedDetails = details
      .Values.Where(x => !native.OwnedDispatchIds.Contains(x.Id))
      .Concat(native.Loads.Select(DispatchProjection.FromExecution))
      .ToDictionary(x => (x.Id, x.ExecutionLegId));
    var clocks = request.IncludeHos
      ? await hos.GetClocksAsync(cancellationToken)
      : null;
    var truckIds = page.Where(x => x.TruckId.HasValue)
      .Select(x => x.TruckId!.Value)
      .ToArray();
    var drivers = clocks is null
      ? new Dictionary<Guid, string>()
      : await dbContext
        .Trucks.AsNoTracking()
        .Where(x => truckIds.Contains(x.Id))
        .Select(x => new
        {
          x.Id,
          ExternalId = x.Driver == null ? "" : x.Driver.ExternalId,
        })
        .ToDictionaryAsync(x => x.Id, x => x.ExternalId, cancellationToken);
    var nativeDriverIds = native
      .Loads.Select(x => x.Work)
      .Where(x => x.ExecutionStatus == "active" && x.DriverId.HasValue)
      .Select(x => x.DriverId!.Value)
      .Distinct()
      .ToArray();
    var nativeDrivers =
      clocks is null || nativeDriverIds.Length == 0
        ? new Dictionary<Guid, string>()
        : await dbContext
          .Drivers.AsNoTracking()
          .Where(x => nativeDriverIds.Contains(x.Id))
          .ToDictionaryAsync(x => x.Id, x => x.ExternalId, cancellationToken);
    foreach (var row in page)
    {
      row.Dispatches = row
        .Dispatches.Where(x =>
          scopedDetails.ContainsKey((x.Id, x.ExecutionLegId))
        )
        .Select(x => scopedDetails[(x.Id, x.ExecutionLegId)].CopyForBoardRow())
        .ToList();
      var activeLeg = row.Dispatches.FirstOrDefault(x =>
        x.ExecutionStatus == "active"
      );
      if (activeLeg is not null)
      {
        row.DriverName = activeLeg.DriverName;
        row.TrailerNumber = activeLeg.TrailerNumber;
        row.Hos =
          activeLeg.DriverId is { } nativeDriver
          && clocks is not null
          && nativeDrivers.TryGetValue(nativeDriver, out var externalId)
            ? clocks.GetValueOrDefault(externalId)
            : null;
        continue;
      }
      if (
        row.TruckId is { } id
        && drivers.TryGetValue(id, out var driver)
        && clocks is not null
      )
        row.Hos = clocks.GetValueOrDefault(driver);
    }
    var rowsMs = Take("rows", ref stage);
    if (request.IncludeEta)
      await eta.PopulateAsync(
        scopedDetails.Values.ToArray(),
        cancellationToken,
        page
      );
    var etaMs = Take("eta", ref stage);

    // A slow board says what it spent its time on. Without this the only
    // measurement available was the total, which cannot tell a cold index
    // from a slow forecast, and the stage meters had no reader at all.
    var total =
      indexMs + detailsMs + executionMs + financialsMs + rowsMs + etaMs;
    if (total >= 1000)
      logger.LogInformation(
        "BoardTiming TotalMs={Total} IndexMs={Index} DetailsMs={Details} "
          + "ExecutionMs={Execution} FinancialsMs={Financials} RowsMs={Rows} "
          + "EtaMs={Eta} Loads={Loads} Financials={WithFinancials} Eta={WithEta}",
        Math.Round(total),
        Math.Round(indexMs),
        Math.Round(detailsMs),
        Math.Round(executionMs),
        Math.Round(financialsMs),
        Math.Round(rowsMs),
        Math.Round(etaMs),
        loadIds.Length,
        request.IncludeFinancials,
        request.IncludeEta
      );
    return RequestResponse<PaginatedList<TruckDispatchBoardResponse>>.Ok(
      result
    );
  }

  private static double Take(string name, ref long since)
  {
    var elapsed = Stopwatch.GetElapsedTime(since).TotalMilliseconds;
    PerformanceStages.Elapsed("dispatch-board", name, since);
    since = Stopwatch.GetTimestamp();
    return elapsed;
  }
}
