namespace Server.Tests.Support;

public sealed class ManualTimeProvider(DateTimeOffset? now = null) : TimeProvider
{
  public DateTimeOffset UtcNow { get; set; } = now ?? new DateTimeOffset(2026, 9, 8, 12, 0, 0, TimeSpan.Zero);
  public override DateTimeOffset GetUtcNow() => UtcNow;
  public void Advance(TimeSpan amount) => UtcNow += amount;
}
