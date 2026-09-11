using System.Threading.Channels;
using Microsoft.Extensions.Time.Testing;

namespace Client.Tests.Support;

internal sealed class ScheduledDelayClock : TimeProvider
{
  private readonly FakeTimeProvider clock = new();
  private readonly Channel<TimeSpan> scheduled = Channel.CreateUnbounded<TimeSpan>();
  private int timerCount;
  public int TimerCount => Volatile.Read(ref timerCount);
  public override DateTimeOffset GetUtcNow() => clock.GetUtcNow();

  public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
  {
    var timer = clock.CreateTimer(callback, state, dueTime, period);
    Interlocked.Increment(ref timerCount);
    scheduled.Writer.TryWrite(dueTime);
    return timer;
  }

  public async Task CompleteAsync(Task operation)
  {
    using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(5));
    try
    {
      while (!operation.IsCompleted)
      {
        var nextDelay = scheduled.Reader.ReadAsync(cancellation.Token).AsTask();
        await Task.WhenAny(operation, nextDelay).WaitAsync(cancellation.Token);
        if (operation.IsCompleted) break;
        clock.Advance(await nextDelay);
      }
      await operation;
    }
    finally { cancellation.Cancel(); }
  }
}
