using Application.Features.Synchronization.Options;
using System.Threading.Channels;
using Application.Features.Routing.Models;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;

namespace Application.Features.Routing.Services.Routes;

public sealed class PlanningRefreshQueue(IMemoryCache cache, IOptions<SynchronizationOptions> options)
{
  private readonly Channel<Guid> queue = Channel.CreateBounded<Guid>(256);
  private readonly HashSet<Guid> pending = [];

  public string? Message(Guid id, RoutePlanningState state)
  {
    var key = $"automatic-planning-error:{id}:{PlanningSettingsService.Signature(state.Profile)}";
    if (cache.TryGetValue<string>(key, out var error)) return error;
    if (state.Plan is { InputsChanged: false }) return null;
    lock (pending)
      return pending.Contains(id) ? "Route update queued." : "Route update pending.";
  }

  public bool Enqueue(Guid id, RoutePlanningState? state = null)
  {
    if (state?.Plan is { InputsChanged: false } plan)
    {
      var preparedAt = plan.CalculatedAt;
      var age = DateTime.UtcNow - preparedAt;
      if (age >= TimeSpan.Zero && age < TimeSpan.FromSeconds(options.Value.OnDemandPlanningSeconds))
        return false;
    }
    lock (pending)
    {
      if (pending.Contains(id) || cache.TryGetValue($"planning-refresh:{id}", out _)) return false;
      if (!queue.Writer.TryWrite(id)) return false;
      pending.Add(id);
      return true;
    }
  }

  public IAsyncEnumerable<Guid> ReadAllAsync(CancellationToken ct) => queue.Reader.ReadAllAsync(ct);

  public void Complete(Guid id, bool succeeded)
  {
    lock (pending)
    {
      cache.Set($"planning-refresh:{id}", true, TimeSpan.FromSeconds(succeeded
        ? options.Value.OnDemandPlanningSeconds : options.Value.RetrySeconds));
      pending.Remove(id);
    }
  }
}
