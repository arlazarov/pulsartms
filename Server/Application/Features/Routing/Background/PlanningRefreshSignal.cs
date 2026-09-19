namespace Application.Features.Routing.Background;

public sealed class PlanningRefreshSignal : IDisposable
{
  private readonly SemaphoreSlim signal = new(0, 1);

  public void Pulse()
  {
    try
    {
      signal.Release();
    }
    catch (SemaphoreFullException) { }
  }

  public Task<bool> WaitAsync(CancellationToken ct) =>
    signal.WaitAsync(TimeSpan.FromSeconds(5), ct);

  public void Dispose() => signal.Dispose();
}
