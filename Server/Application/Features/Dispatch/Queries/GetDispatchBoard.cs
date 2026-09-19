using Application.Caching;
using Application.Features.Dispatch.Models;
using Application.Features.Eta.Services;
using Application.Features.Execution.Queries;
using Application.Features.Execution.Services;
using Application.Features.Fleet.Interfaces;
using Application.Features.Routing.Services.Deadheads;
using Application.Features.Synchronization.Services;
using Application.Models;

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
) : IRequest<RequestResponse<PaginatedList<TruckDispatchBoardResponse>>>;

public class GetDispatchBoardValidator
  : AbstractValidator<GetDispatchBoardQuery>
{
  public GetDispatchBoardValidator()
  {
    RuleFor(x => x.Page).InclusiveBetween(1, 1000000);
    RuleFor(x => x.PageSize).InclusiveBetween(1, 100);
    RuleFor(x => x.Search).MaximumLength(200);
  }
}

public class GetDispatchBoardHandler(
  IAppDbContext dbContext,
  ReadCache reads,
  IDriverHosProvider hos,
  DeadheadService deadhead,
  EtaForecastService eta
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
            request.TruckId,
            request.IncludePlanned,
            request.IncludeOverdue,
            cancellationToken
          )
        ).Select(DispatchWorkProjection.ToBoardRow)
      );
    var index = await reads.GetAsync(
      "board",
      $"index:{date:O}:{request.TruckId}:{request.IncludePlanned}:{request.IncludeOverdue}",
      LoadIndex
    );
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
    var detailRows = await dbContext
      .Dispatches.AsNoTracking()
      .Where(x => loadIds.Contains(x.Id))
      .Select(DispatchProjection.Details)
      .Select(detail => new
      {
        Detail = detail,
        NativeOwned = dbContext.LoadExecutionLegs.Any(link =>
          link.DispatchId == detail.Id
        ),
      })
      .ToListAsync(cancellationToken);
    var details = detailRows.ToDictionary(x => x.Detail.Id, x => x.Detail);
    foreach (var detail in details.Values)
      DispatchProjection.Complete(detail);
    var legIds = page.SelectMany(x => x.Dispatches)
      .Where(x => x.ExecutionLegId.HasValue)
      .Select(x => x.ExecutionLegId!.Value)
      .Distinct()
      .ToArray();
    var native =
      legIds.Length == 0
        ? new TruckExecutionLoads(
          [],
          detailRows
            .Where(x => x.NativeOwned)
            .Select(x => x.Detail.Id)
            .ToHashSet()
        )
        : await ExecutionLoads.ReadAsync(
          dbContext,
          request.TruckId,
          loadIds,
          cancellationToken,
          legIds
        );
    if (request.IncludeFinancials)
      await deadhead.ReadAsync(
        details
          .Values.Where(x => !native.OwnedDispatchIds.Contains(x.Id))
          .ToArray(),
        cancellationToken
      );
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
    if (request.IncludeEta)
      await eta.PopulateAsync(
        scopedDetails.Values.ToArray(),
        cancellationToken,
        page
      );
    return RequestResponse<PaginatedList<TruckDispatchBoardResponse>>.Ok(
      result
    );
  }
}
