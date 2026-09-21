using Domain.Entities.Dispatch;
using Domain.Models.Execution;
using Load = Domain.Entities.Dispatch.Dispatch;

namespace Application.Features.Execution.Services;

internal static class SwitchSourceAssignment
{
  public static async Task<string?> StatusAsync(
    IAppDbContext db,
    Load load,
    SwitchLoadChange change,
    IReadOnlyList<DispatchStop> before,
    bool confirmCompleted,
    CancellationToken ct
  )
  {
    var active = load.Status == "in_transit" || before.Any(x => x.IsCompleted);
    if (
      load.Status is not ("assigned" or "unassigned" or "in_transit")
      || confirmCompleted && !active
      || before.Any(x => x.StateAfter == "No truck")
      || !SwitchPlanningRules.BootstrapMatches(load, change, before)
      || await db.LoadExecutionLegs.AnyAsync(x => x.DispatchId == load.Id, ct)
      || active
        && await ExecutionResources.ConflictsAsync(
          db,
          change.Outgoing,
          null,
          ct
        )
    )
      return null;
    return active ? "active" : "planned";
  }
}
