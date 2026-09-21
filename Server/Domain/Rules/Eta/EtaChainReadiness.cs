using Domain.Models.Execution;
using Domain.Models.Routing;

namespace Domain.Rules.Eta;

// Why a load further down the chain has no arrival time yet, in the words
// the dispatcher reads. Every one of these is something that has to exist
// first - a saved road, a settled order of work, a driver who stays the
// same - so each sentence names the thing that is missing rather than
// reporting a failure.
public static class EtaChainReadiness
{
  public const string MissingConnection =
    "ETA unavailable: waiting for the saved connection from the preceding load.";

  public const string MissingRoad =
    "ETA unavailable: waiting for the saved load route.";

  public const string IncompleteTravelTimes =
    "ETA unavailable: incomplete saved road travel times.";

  public const string NextLoadDriverChanged =
    "ETA unavailable: the next load has a different driver assignment.";

  public const string CurrentDriverChanged =
    "ETA unavailable: waiting for the receiving driver's truck assignment and HOS.";

  public static string? SequenceReason(
    WorkSequenceAssessment sequence,
    RouteWorkSnapshot load
  ) =>
    sequence
      .Issues.FirstOrDefault(x =>
        x.Work == new WorkIdentity(load.Id, load.ExecutionLegId)
      )
      ?.Problem switch
    {
      WorkSequenceProblem.CompetingCurrentWork =>
        "ETA unavailable: multiple loads have started; confirm the current work.",
      WorkSequenceProblem.UnknownOrder =>
        "ETA unavailable: the order of assigned work is unresolved.",
      WorkSequenceProblem.ConflictingOrder =>
        "ETA unavailable: assigned work conflicts with its predecessor order.",
      WorkSequenceProblem.MissingExecutionLink =>
        "ETA unavailable: the execution link is missing.",
      WorkSequenceProblem.AwaitingTransfer =>
        "ETA unavailable: waiting for confirmed release and receipt.",
      _ => null,
    };

  // The chain is one driver's run. A load whose assignment - or whose
  // remaining stops - name a different driver is that driver's work, and
  // this truck's hours say nothing about when it arrives.
  public static bool DriverChanged(RouteWorkSnapshot load, Guid? driverId) =>
    load.DriverId.HasValue && load.DriverId != driverId
    || load.Stops.Any(s => !s.IsCompleted && s.DriverId != driverId);
}
