namespace Domain.Rules;

public sealed class RoutePlanningException(
  string message,
  DateTime? retryAfter = null,
  bool busy = false,
  bool dependencyChanged = false
) : Exception(message)
{
  public DateTime RetryAfter { get; } = retryAfter ?? DateTime.MaxValue;

  // Another planning pass holds the truck's inputs for a moment. It is
  // expected contention: answered as a conflict to retry, never logged as a
  // failure. A provider asking to back off also has a retry time, but is not
  // this.
  public bool Busy { get; } = busy;

  // Something the work was calculated from has demonstrably changed since
  // it was read: a result made from it no longer holds. Only the checks that
  // compare a captured version with the current one say this.
  public bool DependencyChanged { get; } = dependencyChanged;

  public static RoutePlanningException Changed(string message) =>
    new(message, dependencyChanged: true);

  public static RoutePlanningException InputsBusy(DateTime retryAfter) =>
    new(
      "Planning inputs are being updated. Retry planning shortly.",
      retryAfter,
      busy: true
    );
}
