namespace Domain.Rules;

public sealed class RoutePlanningException(
  string message,
  DateTime? retryAfter = null,
  bool busy = false
) : Exception(message)
{
  public DateTime RetryAfter { get; } = retryAfter ?? DateTime.MaxValue;

  // Another planning pass holds the truck's inputs for a moment. It is
  // expected contention: answered as a conflict to retry, never logged as a
  // failure. A provider asking to back off also has a retry time, but is not
  // this.
  public bool Busy { get; } = busy;

  public static RoutePlanningException InputsBusy(DateTime retryAfter) =>
    new(
      "Planning inputs are being updated. Retry planning shortly.",
      retryAfter,
      busy: true
    );
}
