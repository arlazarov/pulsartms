namespace Domain.Rules;

// Whether a load is done: closed by the source or a dispatcher, or its
// cargo delivered and the truck's work on it finished (CargoDelivery,
// TruckWorkCompletion). The three are distinct facts; this only combines
// them for the "Completed" a load carries. A load whose cargo is delivered
// while the truck still has a trailer to drop is not completed yet.
//
// This was decided in the browser, from a copy of the stops, by a rule the
// server could not see; then here, by a reading of "the last stop" that
// ExecutionWorkRelevance did not share (stage 4b of
// docs/architecture/current-work.md).
public static class LoadCompletion
{
  // The closed status as a database compares it: against a lowered column.
  public const string ClosedStatus = "completed";

  public static bool IsClosed(string status) =>
    status.Equals(ClosedStatus, StringComparison.OrdinalIgnoreCase);

  public static bool IsCompleted(
    string status,
    IEnumerable<CompletionStop> stops
  ) => IsCompleted(status, WorkCompletion.Of(stops));

  public static bool IsCompleted(string status, CompletionFacts facts) =>
    IsClosed(status) || facts.TruckWorkFinished;
}
