namespace Domain.Entities.Dispatch;

public static class StopCompletion
{
  public static bool IsCompleted(
    bool awaitingHandoff,
    bool? completionOverride,
    bool executionCompleted,
    bool hasActual
  ) =>
    !awaitingHandoff
    && (completionOverride ?? (executionCompleted || hasActual));
}
