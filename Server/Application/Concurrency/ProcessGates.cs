namespace Application.Concurrency;

// Process-wide coordination, not a feature concern: the import loop and the
// user commands that touch the same work take the same gate. A second
// application instance does not share these, so they bound work inside one
// process rather than establish exclusivity.
public static class ProcessGates
{
  public static readonly SemaphoreSlim Fleet = new(1, 1);
  public static readonly SemaphoreSlim Dispatch = new(1, 1);
}
