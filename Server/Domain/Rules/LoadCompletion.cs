namespace Domain.Rules;

// Whether a load is done.
//
// It is when it has been marked so. Otherwise it is when its last stop is a
// delivery and that delivery is done: said to be by a dispatcher's override,
// or, unless a dispatcher has said it is not, recorded by the truck leaving
// or the freight being delivered - or confirmed by hand, which counts only
// once every other stop that carries cargo is done too. A delivery confirmed
// by hand on a load whose pickup never happened is a mistake, not a
// completed load.
//
// This was decided in the browser, from a copy of the stops, by a rule the
// server could not see - and the browser's copy of "a stop is done" had
// already drifted from the server's: it did not know a stop can be waiting
// for a handoff.
public static class LoadCompletion
{
  public static bool IsCompleted(
    string status,
    string? finalStopJob,
    bool? finalStopOverride,
    bool finalStopRecorded,
    bool finalStopConfirmedByHand,
    bool everyCargoStopCompleted
  ) =>
    status.Equals("completed", StringComparison.OrdinalIgnoreCase)
    || finalStopJob is not null
      && (
        finalStopJob.Equals("Drop Off", StringComparison.OrdinalIgnoreCase)
        || finalStopJob.Equals("Delivery", StringComparison.OrdinalIgnoreCase)
      )
      && (
        finalStopOverride == true
        || finalStopOverride != false
          && (
            finalStopRecorded
            || finalStopConfirmedByHand && everyCargoStopCompleted
          )
      );
}
