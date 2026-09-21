namespace Domain.Rules;

public sealed class RoutePlanningException(
  string message,
  DateTime? retryAfter = null
) : Exception(message)
{
  public DateTime RetryAfter { get; } = retryAfter ?? DateTime.MaxValue;
}
