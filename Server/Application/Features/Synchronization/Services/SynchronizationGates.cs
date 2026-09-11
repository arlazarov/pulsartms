namespace Application.Features.Synchronization.Services;

public static class SynchronizationGates
{
  public static readonly SemaphoreSlim Fleet = new(1, 1);
  public static readonly SemaphoreSlim Dispatch = new(1, 1);
}
