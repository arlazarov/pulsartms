using System.Threading.Channels;
using Application.Features.Fleet.Interfaces;
using Domain.Models.Fleet;

namespace Application.Features.Fleet.Services;

public sealed class DriverHosSnapshot(TimeProvider time) : IDriverHosProvider
{
  private sealed record Clock(
    long? Break,
    long? Drive,
    long? Shift,
    long? Cycle,
    DateTime UpdatedAt,
    string? Duty
  );

  private readonly object gate = new();
  private readonly Channel<bool> demand = Channel.CreateBounded<bool>(
    new BoundedChannelOptions(1)
    {
      SingleReader = true,
      FullMode = BoundedChannelFullMode.DropWrite,
    }
  );
  private IReadOnlyDictionary<string, Clock> clocks =
    new Dictionary<string, Clock>();
  private DateTimeOffset requestedAt = DateTimeOffset.MinValue;
  private DateTimeOffset nextRefresh = DateTimeOffset.MinValue;
  private bool refreshing;

  public Task<IReadOnlyDictionary<string, DriverHosClocks>> GetClocksAsync(
    CancellationToken ct
  )
  {
    ct.ThrowIfCancellationRequested();
    var now = time.GetUtcNow();
    IReadOnlyDictionary<string, Clock> current;
    lock (gate)
    {
      requestedAt = now;
      current = clocks;
    }
    demand.Writer.TryWrite(true);
    return Task.FromResult<IReadOnlyDictionary<string, DriverHosClocks>>(
      current
        .Where(x =>
          x.Value.UpdatedAt <= now.UtcDateTime
          && x.Value.UpdatedAt > now.UtcDateTime.AddMinutes(-1)
        )
        .ToDictionary(
          x => x.Key,
          x => new DriverHosClocks
          {
            BreakMs = x.Value.Break,
            DriveMs = x.Value.Drive,
            ShiftMs = x.Value.Shift,
            CycleMs = x.Value.Cycle,
            UpdatedAt = x.Value.UpdatedAt,
            CurrentDutyStatus = x.Value.Duty,
          },
          StringComparer.Ordinal
        )
    );
  }

  public bool TryBeginRefresh(bool keepWarm)
  {
    var now = time.GetUtcNow();
    lock (gate)
    {
      if (
        refreshing
        || nextRefresh > now
        || !keepWarm && requestedAt < now.AddMinutes(-10)
      )
        return false;
      refreshing = true;
      return true;
    }
  }

  public void Complete(IReadOnlyDictionary<string, DriverHosClocks>? values)
  {
    var now = time.GetUtcNow();
    var bounded =
      values is { Count: <= 10000 }
      && values.Sum(x =>
        128L + x.Key.Length * 2L + (x.Value.CurrentDutyStatus?.Length ?? 0) * 2L
      )
        <= 4 * 1024 * 1024;
    var captured = bounded
      ? values!.ToDictionary(
        x => x.Key,
        x => new Clock(
          x.Value.BreakMs,
          x.Value.DriveMs,
          x.Value.ShiftMs,
          x.Value.CycleMs,
          x.Value.UpdatedAt,
          x.Value.CurrentDutyStatus
        ),
        StringComparer.Ordinal
      )
      : null;
    lock (gate)
    {
      if (captured is not null)
        clocks = captured;
      nextRefresh = now.AddSeconds(captured is { Count: > 0 } ? 45 : 60);
      refreshing = false;
    }
  }

  public async Task WaitForRefreshAsync(CancellationToken ct)
  {
    using var timeout = new CancellationTokenSource(
      TimeSpan.FromSeconds(5),
      time
    );
    using var linked = CancellationTokenSource.CreateLinkedTokenSource(
      ct,
      timeout.Token
    );
    try
    {
      await demand.Reader.ReadAsync(linked.Token);
    }
    catch (OperationCanceledException) when (!ct.IsCancellationRequested) { }
  }
}
