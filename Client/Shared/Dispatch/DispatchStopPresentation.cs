using Client.Models.DTO.Dispatch;

namespace Client.Shared.Dispatch;

public sealed record DispatchStopVisit(
  DispatchStopResponse Stop,
  int Number,
  int VisitNumber,
  int VisitCount
)
{
  public string VisitLabel =>
    VisitCount > 1 ? $"Visit {VisitNumber} of {VisitCount}" : "";
}

public static class DispatchStopPresentation
{
  public static bool ShowsCargo(DispatchStopResponse stop)
  {
    if (
      stop.DriverOnly
      || Job(stop) == "DRIVER START"
      || stop.StateAfter == "No truck"
    )
      return false;
    // Delivery cargo describes the unloading action, not the resulting empty
    // state.
    if (
      Job(stop)
      is "PICKUP"
        or "PICK UP"
        or "DELIVERY"
        or "DROP OFF"
        or "DROPOFF"
    )
      return true;
    if (stop.StateAfter is "Bobtail" or "Empty")
      return false;
    return stop.StateAfter == "Loaded"
      || Job(stop)
        is not ("COLLECT TRUCK" or "COLLECT TRAILER" or "DROP TRAILER");
  }

  public static bool HasSupplementalFacts(DispatchStopResponse stop) =>
    !string.IsNullOrWhiteSpace(stop.ZipCode)
    || !string.IsNullOrWhiteSpace(stop.Country)
    || !string.IsNullOrWhiteSpace(stop.Notes)
    || ShowsCargo(stop)
      && (
        !string.IsNullOrWhiteSpace(stop.Commodity)
        || stop.Weight.HasValue
        || stop.Pieces.HasValue
        || stop.Pallets.HasValue
        || !string.IsNullOrWhiteSpace(stop.Temperature)
      );

  public static string CompletionRevision(
    IEnumerable<DispatchStopResponse> stops
  ) =>
    string.Join(
      ",",
      stops
        .Where(s => s.ManualCompletionRevision != 0)
        .OrderBy(s => s.Id)
        .Select(s => $"{s.Id}:{s.ManualCompletionRevision}")
    );

  public static IReadOnlyList<DispatchStopVisit> OrderedVisits(
    IEnumerable<DispatchStopResponse> stops
  )
  {
    var ordered = stops.OrderBy(stop => stop.Sequence).ToArray();
    var addresses = ordered.Select(AddressIdentity).ToArray();
    var counts = addresses
      .Where(address => address.Length > 0)
      .GroupBy(address => address)
      .ToDictionary(group => group.Key, group => group.Count());
    var visits = new Dictionary<string, int>();
    return ordered
      .Select(
        (stop, index) =>
        {
          var address = addresses[index];
          if (address.Length == 0)
            return new DispatchStopVisit(stop, index + 1, 1, 1);
          var number = visits.GetValueOrDefault(address) + 1;
          visits[address] = number;
          return new DispatchStopVisit(
            stop,
            index + 1,
            number,
            counts[address]
          );
        }
      )
      .ToArray();
  }

  public static string Summary(IEnumerable<DispatchStopResponse> stops)
  {
    var values = stops.ToArray();
    var pickups = values.Count(stop => Job(stop) is "PICKUP" or "PICK UP");
    var deliveries = values.Count(stop =>
      Job(stop) is "DELIVERY" or "DROP OFF" or "DROPOFF"
    );
    var parts = new List<string> { Quantity(values.Length, "stop") };
    if (pickups > 0)
      parts.Add(Quantity(pickups, "pickup"));
    if (deliveries > 0)
      parts.Add(deliveries == 1 ? "1 delivery" : $"{deliveries} deliveries");
    return string.Join(" · ", parts);
  }

  private static string Quantity(int count, string noun) =>
    $"{count} {noun}{(count == 1 ? "" : "s")}";

  private static string Job(DispatchStopResponse stop) => Normalize(stop.Job);

  private static string AddressIdentity(DispatchStopResponse stop)
  {
    if (
      string.IsNullOrWhiteSpace(stop.Address)
      || (
        (
          string.IsNullOrWhiteSpace(stop.City)
          || string.IsNullOrWhiteSpace(stop.Province)
        ) && string.IsNullOrWhiteSpace(stop.ZipCode)
      )
    )
      return "";
    return string.Join(
      '\u001f',
      new[]
      {
        stop.Address,
        stop.City,
        stop.Province,
        stop.ZipCode,
        stop.Country,
      }.Select(Normalize)
    );
  }

  private static string Normalize(string? value) =>
    string.Join(
        ' ',
        (value ?? "").Split(
          (char[]?)null,
          StringSplitOptions.RemoveEmptyEntries
        )
      )
      .ToUpperInvariant();
}
