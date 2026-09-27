using Client.Models.DTO.Dispatch;

namespace Client.Shared.Dispatch;

// How a load's place on its truck reads on every dispatch view. The server
// places the load (DispatchResponse.WorkPhase) from the planning inputs;
// this only names the place. A stale or unknown place is never read as
// planned or upcoming.
public static class DispatchWorkPhase
{
  public static bool IsCurrent(DispatchResponse load) =>
    !load.Completed && load.WorkPhase == "current";

  public static bool IsNext(DispatchResponse load) =>
    !load.Completed && load.WorkPhase == "next";

  public static bool IsEarlier(DispatchResponse load) =>
    !load.Completed && load.WorkPhase == "earlier";

  // Null where the server holds the load but plans no place for it, or the
  // row has no truck: a view may then say what it knows (planned).
  public static string? Label(DispatchResponse load) =>
    load.Completed ? "Completed" : Label(load.WorkPhase);

  // The same for a view that has only the server's placement (Messenger).
  public static string? Label(string? phase) =>
    phase switch
    {
      "current" => "Current",
      "next" => "Next",
      "upcoming" => "Upcoming",
      // The row and the inputs disagree on the assignment; nothing says a
      // refresh is under way, so none is claimed.
      "stale" => "Needs refresh",
      "earlier" or "unknown" => "",
      _ => null,
    };

  // The server's conflict in words, the same on every view.
  public static string? ConflictText(string? conflict) =>
    conflict switch
    {
      "route_passed_not_delivered" => "Route passed · not delivered",
      "route_passed_work_open" => "Route passed · delivered, work open",
      _ => null,
    };
}
