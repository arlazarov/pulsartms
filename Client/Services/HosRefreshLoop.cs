namespace Client.Services;

public static class HosRefreshLoop
{
  public static Task RunAsync(
    Func<CancellationToken, Task<bool>> refresh,
    CancellationToken ct,
    TimeProvider clock,
    PageVisibility visibility
  )
  {
    var attempts = 0;
    var ready = false;
    var next = DateTimeOffset.MinValue;
    return RefreshLoop.RunAsync(
      async token =>
      {
        if (clock.GetUtcNow() < next)
          return;
        next = clock.GetUtcNow().AddSeconds(15);
        ready |= await refresh(token);
        if (!ready && ++attempts < 5)
          next = clock.GetUtcNow().AddSeconds(1);
      },
      _ => Task.CompletedTask,
      TimeSpan.FromSeconds(1),
      ct,
      clock,
      visibility
    );
  }
}
