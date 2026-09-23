namespace Domain.Rules;

// What a load the source cancelled does to the execution already accepted
// for it.
//
// Work nobody started and nobody here touched is cancelled with the load.
// Anything else is held: it leaves the truck's current and future work -
// the board, map selection, the fuel horizon and the ETA stop reading it,
// because they read only active and planned legs - but it is not completed
// and not cancelled either. Its stops, movements and revisions stay, and a
// dispatcher closes it explicitly once they have looked.
public static class SourceCancellation
{
  public const string Held = "held";

  public const string HeldReason =
    "The source cancelled this load after its execution started or was "
    + "changed here. It is no longer planned work; review it, then close it.";

  public static string Outcome(bool started, bool changedHere) =>
    started || changedHere ? Held : "cancelled";
}
