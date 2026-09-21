namespace Domain.Rules;

// The words a load arrives wearing.
//
// A load's status and its stops' jobs are written by whoever sent the load
// and are never normalised: DispatchMapper assigns source.Status and
// source.Job straight through. So "is this a pickup" and "has this been
// cancelled" are questions about someone else's spelling, and every place
// that asks them has to accept the same set of answers.
//
// They did not. Counting only the stop's job, there were nine different
// readings across the server and the browser - some accepting "Pickup"
// beside "Pick Up", some not, some comparing case-insensitively and some
// not, three of them inside one file. Two were wrong in ways a driver
// would see: an empty connection was not built at all for a load whose
// pickup was spelled "Pickup", and a load the broker cancelled as
// "canceled" still owned the connection after it.
//
// Ask here instead. A new spelling is then one line, in one place.
public static class SourceWords
{
  private static bool Is(string? value, string word) =>
    value is not null
    && value
      .Replace(" ", "")
      .Equals(word.Replace(" ", ""), StringComparison.OrdinalIgnoreCase);

  // "Pick Up", "Pickup", "pickup" - the same stop.
  public static bool IsPickup(string? job) => Is(job, "Pick Up");

  // A delivery is written either way by different brokers.
  public static bool IsDelivery(string? job) =>
    Is(job, "Drop Off") || Is(job, "Delivery");

  // A stop that moves freight, as opposed to collecting a truck or a
  // trailer or starting a driver's day.
  public static bool MovesCargo(string? job) =>
    IsPickup(job) || IsDelivery(job);

  // British and American spellings both arrive. Reading only one of them
  // once carried a cancelled load across the map.
  public static bool IsCancelled(string? status) =>
    Is(status, "cancelled") || Is(status, "canceled");

  // The same question in the form a database can answer, for the reads
  // that ask it in SQL. Compare against a lowered column.
  public static readonly string[] Cancelled = ["cancelled", "canceled"];
}
