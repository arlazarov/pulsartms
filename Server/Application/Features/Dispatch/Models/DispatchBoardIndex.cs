using Application.Models;

namespace Application.Features.Dispatch.Models;

// Private immutable search data can be shared across requests. Only the selected
// page becomes mutable response DTOs; no caller can mutate cached rows or loads.
public sealed class DispatchBoardIndex
{
  private sealed record Load(Guid Id, string Number, string Order, string Customer, string Driver, string[] Stops);
  private sealed record Row(string Key, Guid? TruckId, string Truck, string Driver, string Trailer, Load[] Loads);
  private readonly Row[] rows;
  public long EstimatedBytes { get; }

  public DispatchBoardIndex(IEnumerable<TruckDispatchBoardResponse> source)
  {
    rows = source.OrderBy(x => x.Key == "unassigned" ? 1 : 0)
      .ThenBy(x => long.TryParse(x.TruckNumber, out var n) ? n : long.MaxValue)
      .ThenBy(x => x.TruckNumber).ThenBy(x => x.Key)
      .Select(x => new Row(x.Key, x.TruckId, x.TruckNumber, x.DriverName, x.TrailerNumber,
        x.Dispatches.Select(d => new Load(d.Id, d.LoadNumber.ToString(), d.OrderNumber, d.CustomerName, d.DriverName,
          d.Stops.SelectMany(s => new[] {s.City, s.Name}).ToArray())).ToArray())).ToArray();
    // Conservative accounting, including repeated strings rather than assuming
    // interning/sharing. Keep the existing cache's per-entry and total bounds.
    static long Text(string s) => 32L + 2L * s.Length;
    EstimatedBytes = 64L + rows.Sum(r => 160L + Text(r.Key) + Text(r.Truck) + Text(r.Driver) + Text(r.Trailer)
      + r.Loads.Sum(l => 128L + Text(l.Number) + Text(l.Order) + Text(l.Customer) + Text(l.Driver)
        + l.Stops.Sum(s => 8L + Text(s))));
  }

  public PaginatedList<TruckDispatchBoardResponse> SelectPage(int page, int pageSize, string? query, Guid? truckId)
  {
    IEnumerable<Row> result = rows;
    if (truckId.HasValue) result = result.Where(x => x.TruckId == truckId);
    if (!string.IsNullOrWhiteSpace(query))
    {
      var search = query.Trim();
      bool Matches(string value) => value.Contains(search, StringComparison.OrdinalIgnoreCase);
      bool Prefix(string value) => value.StartsWith(search, StringComparison.OrdinalIgnoreCase);
      var exact = result.Where(x => x.Truck.Equals(search, StringComparison.OrdinalIgnoreCase));
      var equipment = result.Where(x => Prefix(x.Truck) || Matches(x.Driver) || Prefix(x.Trailer));
      result = exact.Any() ? exact : equipment.Any() ? equipment : result.Where(x =>
        x.Loads.Any(d => Prefix(d.Number) || Prefix(d.Order) || Matches(d.Customer)
          || Matches(d.Driver) || d.Stops.Any(Matches)));
    }
    var count = 0;
    var skip = (long)(page - 1) * pageSize;
    var items = new List<TruckDispatchBoardResponse>();
    foreach (var row in result)
    {
      if (count++ < skip || items.Count >= pageSize) continue;
      items.Add(new() {Key = row.Key, TruckId = row.TruckId, TruckNumber = row.Truck,
        DriverName = row.Driver, TrailerNumber = row.Trailer,
        Dispatches = row.Loads.Select(x => new DispatchResponse {Id = x.Id}).ToList()});
    }
    return new() {Items = items, Page = page, PageSize = pageSize, TotalCount = count};
  }
}
