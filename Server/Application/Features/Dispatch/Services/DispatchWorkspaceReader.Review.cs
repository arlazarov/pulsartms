using Domain.Entities.Dispatch;
using Domain.Entities.Execution;

namespace Application.Features.Dispatch.Services;

// What a load's workspace has to say about itself before a dispatcher
// acts on it: which stops are ordinary freight movements, and the one
// sentence naming everything about this load that still needs a look.
public static partial class DispatchWorkspaceReader
{
  private static bool Ordinary(string job) =>
    job is "Pick Up" or "Pickup" or "Drop Off" or "Delivery";

  private static string? ReviewReason(
    DispatchWorkspace? workspace,
    DispatchSourceLink? sourceLink,
    List<ExecutionLeg> legs,
    bool pendingAssignment
  )
  {
    var reasons = legs.Select(x => x.SourceReviewReason)
      .Prepend(workspace?.SourceReviewReason)
      .Prepend(legs.Count == 0 ? sourceLink?.ExecutionReviewReason : null)
      .Append(
        pendingAssignment
          ? "Initial execution is not accepted. Review resources, visit times and execution boundaries."
          : null
      )
      .Where(x => !string.IsNullOrWhiteSpace(x))
      .Distinct()
      .ToArray();
    return reasons.Length == 0 ? null : string.Join(" ", reasons);
  }
}
