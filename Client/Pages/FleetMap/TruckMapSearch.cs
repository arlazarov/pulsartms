using Client.Models.DTO.Fleet;

namespace Client.Pages.FleetMap;

public static class TruckMapSearch
{
  public static List<TruckLocationMapDto> Filter(
    List<TruckLocationMapDto> trucks,
    string? query
  )
  {
    var term = query?.Trim() ?? "";
    if (term.Length == 0)
      return trucks;
    var exact = trucks
      .Where(x =>
        string.Equals(x.UnitNumber, term, StringComparison.OrdinalIgnoreCase)
      )
      .ToList();
    return exact.Count > 0
      ? exact
      : trucks
        .Where(x =>
          x.UnitNumber.StartsWith(term, StringComparison.OrdinalIgnoreCase)
          || x.DriverName.Contains(term, StringComparison.OrdinalIgnoreCase)
          || x.TrailerNumber.StartsWith(
            term,
            StringComparison.OrdinalIgnoreCase
          )
        )
        .ToList();
  }
}
