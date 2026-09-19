using Application.Caching;

namespace Application.Features.Synchronization.Services;

// Fleet and dispatch synchronization each run one at a time per process, from any trigger.
public sealed class SynchronizationGates(ProcessGates gates)
{
  public SemaphoreSlim Fleet { get; } = gates.Slots<FleetScope>(1);
  public SemaphoreSlim Dispatch { get; } = gates.Slots<DispatchScope>(1);
  private sealed class FleetScope;
  private sealed class DispatchScope;
}
