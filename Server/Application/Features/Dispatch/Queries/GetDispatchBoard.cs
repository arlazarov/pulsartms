using Application.Features.Routing.Services.Deadheads;
using Application.Caching;
using Application.Features.Synchronization.Services;
using Application.Features.Dispatch.Models;
using Application.Models;

namespace Application.Features.Dispatch.Queries;

public record GetDispatchBoardQuery(int Page = 1, int PageSize = 12, string? Search = null,
  Guid? TruckId = null, DateOnly? Date = null, bool IncludeHos = true, bool IncludePlanned = false, bool IncludeFinancials = true, bool IncludeEta = true,
  bool IncludeOverdue = false)
  : IRequest<RequestResponse<PaginatedList<TruckDispatchBoardResponse>>>;

public class GetDispatchBoardValidator : AbstractValidator<GetDispatchBoardQuery>
{
  public GetDispatchBoardValidator()
  {
    RuleFor(x => x.Page).InclusiveBetween(1, 1000000);
    RuleFor(x => x.PageSize).InclusiveBetween(1, 100);
    RuleFor(x => x.Search).MaximumLength(200);
  }
}

public class GetDispatchBoardHandler(IAppDbContext dbContext, ReadCache reads,
  Application.Features.Fleet.Interfaces.IDriverHosProvider hos,
  Application.Features.Routing.Services.Deadheads.DeadheadService deadhead,
  Application.Features.Eta.Services.EtaForecastService eta)
  : IRequestHandler<GetDispatchBoardQuery, RequestResponse<PaginatedList<TruckDispatchBoardResponse>>>
{
  public async Task<RequestResponse<PaginatedList<TruckDispatchBoardResponse>>> Handle(
    GetDispatchBoardQuery request, CancellationToken cancellationToken)
  {
    var date = request.Date ?? DateOnly.FromDateTime(DateTime.UtcNow);
    async Task<DispatchBoardIndex> LoadIndex() => new(await LoadRowsAsync(date, false, request.TruckId, request.IncludePlanned, request.IncludeOverdue, cancellationToken));
    var index = await reads.GetAsync("board", $"index:{date:O}:{request.TruckId}:{request.IncludePlanned}:{request.IncludeOverdue}", LoadIndex);
    var result = index.SelectPage(request.Page, request.PageSize, request.Search, request.TruckId);
    var page = result.Items;
    var loadIds = page.SelectMany(x => x.Dispatches).Select(x => x.Id).Distinct().ToArray();
    var details = await dbContext.Dispatches.AsNoTracking().Where(x => loadIds.Contains(x.Id))
      .Select(DispatchProjection.Details).ToDictionaryAsync(x => x.Id, cancellationToken);
    if (request.IncludeFinancials) await deadhead.ReadAsync(details.Values.ToArray(), cancellationToken);
    var clocks = request.IncludeHos ? await hos.GetClocksAsync(cancellationToken) : null;
    var truckIds = page.Where(x => x.TruckId.HasValue).Select(x => x.TruckId!.Value).ToArray();
    var drivers = clocks is null ? new Dictionary<Guid, string>() : await dbContext.Trucks.AsNoTracking()
      .Where(x => truckIds.Contains(x.Id)).Select(x => new { x.Id, ExternalId = x.Driver == null ? "" : x.Driver.ExternalId })
      .ToDictionaryAsync(x => x.Id, x => x.ExternalId, cancellationToken);
    foreach (var row in page)
    {
      row.Dispatches = row.Dispatches.Where(x => details.ContainsKey(x.Id)).Select(x => details[x.Id].CopyForBoardRow()).ToList();
      if (row.TruckId is { } id && drivers.TryGetValue(id, out var driver) && clocks is not null)
        row.Hos = clocks.GetValueOrDefault(driver);
    }
    if (request.IncludeEta) await eta.PopulateAsync(details.Values.ToArray(), cancellationToken, page);
    return RequestResponse<PaginatedList<TruckDispatchBoardResponse>>.Ok(result);
  }

  private async Task<List<TruckDispatchBoardResponse>> LoadRowsAsync(DateOnly date, bool includeHos, Guid? truckId, bool includePlanned, bool includeOverdue, CancellationToken cancellationToken)
  {
    var clocks = !includeHos || hos is null ? null : await hos.GetClocksAsync(cancellationToken);
    var fleet = await dbContext.Trucks.AsNoTracking().Select(x => new
    {
      x.Id, x.UnitNumber, x.IsActive,
      DriverExternalId = x.Driver != null ? x.Driver.ExternalId : "",
      DriverName = x.Driver != null ? x.Driver.Name : "",
      TrailerNumber = x.Trailer != null ? x.Trailer.UnitNumber : "",
    }).ToListAsync(cancellationToken);
    var byId = fleet.ToDictionary(x => x.Id);
    var byNumber = fleet.GroupBy(x => x.UnitNumber.Trim(), StringComparer.OrdinalIgnoreCase)
      .ToDictionary(x => x.Key, x => x.First(), StringComparer.OrdinalIgnoreCase);
    var query = dbContext.Dispatches.AsNoTracking().Where(x => x.Status == "assigned" || x.Status == "in_transit"
      || includePlanned && (x.Status == "planned" || x.Status == "unassigned"));
    if (truckId.HasValue)
    {
      var knownIds = fleet.Select(x => x.Id).ToArray();
      var numbers = byNumber.Where(x => x.Value.Id == truckId).Select(x => x.Key.ToUpperInvariant()).ToArray();
      query = query.Where(x => x.TruckId == truckId
        || ((!x.TruckId.HasValue || !knownIds.Contains(x.TruckId.Value)) && numbers.Contains(x.TruckNumber.Trim().ToUpper()))
        || x.Stops.Any(s => s.TruckId == truckId
          || ((!s.TruckId.HasValue || !knownIds.Contains(s.TruckId.Value)) && numbers.Contains(s.TruckNumber.Trim().ToUpper()))));
    }
    var loads = await query.Select(x => new DispatchResponse
    {
      Id = x.Id, TruckId = x.TruckId, TruckNumber = x.TruckNumber, DriverName = x.DriverName,
      TrailerNumber = x.TrailerNumber, LoadNumber = x.LoadNumber, OrderNumber = x.OrderNumber,
      CustomerName = x.CustomerName, Status = x.Status, ShipDate = x.ShipDate, DeliveryDate = x.DeliveryDate,
      Stops = x.Stops.OrderBy(s => s.Sequence).Select(s => new DispatchStopResponse
      {
        Id = s.Id,
        TruckId = s.TruckId, TruckNumber = s.TruckNumber, DriverName = s.DriverName, TrailerNumber = s.TrailerNumber,
        Job = s.Job, City = s.City, Name = s.Name, ScheduledDate = s.ScheduledDate, ScheduledTime = s.ScheduledTime,
        PickedUpAt = s.PickedUpAt, DeliveredAt = s.DeliveredAt, DepartedAt = s.DepartedAt
      }).ToList()
    }).ToListAsync(cancellationToken);
    var rows = fleet.ToDictionary(x => x.Id.ToString(), x => new TruckDispatchBoardResponse
    {
      Hos = clocks is not null && clocks.TryGetValue(x.DriverExternalId, out var clock) ? clock : null,
      Key = x.Id.ToString(), TruckId = x.Id, TruckNumber = x.UnitNumber,
      DriverName = x.DriverName, TrailerNumber = x.TrailerNumber,
    });
    var active = fleet.Where(x => x.IsActive).Select(x => x.Id.ToString()).ToHashSet();

    foreach (var load in loads.Where(x => IsCurrentOrUpcoming(x, date, includeOverdue))
      .OrderBy(x => HasStarted(x) ? 0 : 1).ThenBy(Start).ThenBy(x => x.LoadNumber))
    {
      var assignments = load.Stops.Select(x => (x.TruckId, x.TruckNumber))
        .Prepend((load.TruckId, load.TruckNumber))
        .Where(x => x.TruckId.HasValue || !string.IsNullOrWhiteSpace(x.TruckNumber)).ToList();
      if (assignments.Count == 0) assignments.Add((null, ""));
      var added = new HashSet<string>();
      foreach (var (id, number) in assignments)
      {
        var truck = id.HasValue ? byId.GetValueOrDefault(id.Value) : null;
        truck ??= string.IsNullOrWhiteSpace(number) ? null : byNumber.GetValueOrDefault(number.Trim());
        var key = truck?.Id.ToString() ?? id?.ToString() ?? (string.IsNullOrWhiteSpace(number)
          ? "unassigned" : "number:" + number.Trim().ToUpperInvariant());
        if (truckId.HasValue && (truck?.Id ?? id) != truckId) continue;
        if (!added.Add(key)) continue;
        if (!rows.TryGetValue(key, out var row))
        {
          row = new() { Key = key, TruckId = truck?.Id ?? id, TruckNumber = number.Trim() };
          rows.Add(key, row);
        }
        row.Dispatches.Add(load);
        var stop = load.Stops.FirstOrDefault(x => x.TruckId == row.TruckId && row.TruckId.HasValue
          || !string.IsNullOrWhiteSpace(row.TruckNumber) && x.TruckNumber.Equals(row.TruckNumber, StringComparison.OrdinalIgnoreCase));
        if (string.IsNullOrWhiteSpace(row.DriverName)) row.DriverName = !string.IsNullOrWhiteSpace(stop?.DriverName) ? stop.DriverName : load.DriverName;
        if (string.IsNullOrWhiteSpace(row.TrailerNumber)) row.TrailerNumber = !string.IsNullOrWhiteSpace(stop?.TrailerNumber) ? stop.TrailerNumber : load.TrailerNumber;
      }
    }

    return rows.Values.Where(x => (!truckId.HasValue || x.TruckId == truckId)
      && (active.Contains(x.Key) || x.Dispatches.Count > 0)).ToList();
  }

  private static bool IsCurrentOrUpcoming(DispatchResponse load, DateOnly date, bool includeOverdue)
  {
    var final = load.Stops.LastOrDefault(x => x.Job.Equals("Drop Off", StringComparison.OrdinalIgnoreCase));
    if (final?.DeliveredAt is not null || final?.DepartedAt is not null) return false;
    // A missed appointment or UTC midnight does not complete an active load.
    if (HasStarted(load)) return true;
    var end = load.DeliveryDate ?? load.Stops.LastOrDefault()?.ScheduledDate ?? load.ShipDate;
    return includeOverdue || end is null || end >= date;
  }

  private static bool HasStarted(DispatchResponse load) =>
    load.Status.Equals("in_transit", StringComparison.OrdinalIgnoreCase)
    || load.Stops.Any(stop => stop.PickedUpAt.HasValue);

  private static DateTime Start(DispatchResponse load) =>
    (load.Stops.FirstOrDefault()?.ScheduledDate ?? load.ShipDate ?? DateOnly.MaxValue)
      .ToDateTime(load.Stops.FirstOrDefault()?.ScheduledTime ?? TimeOnly.MinValue);
}
