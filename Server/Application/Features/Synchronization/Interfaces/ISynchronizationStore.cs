using Application.Features.Synchronization.Models;

namespace Application.Features.Synchronization.Interfaces;

public interface ISynchronizationStore
{
  Task<bool> AcquireAsync(string owner, DateTime now, CancellationToken ct);
  Task<bool> RenewAsync(string owner, DateTime now, CancellationToken ct);
  Task<SynchronizationState> ReadAsync(CancellationToken ct);
  Task SaveAsync(string owner, string json, CancellationToken ct);
  Task ReleaseAsync(string owner, CancellationToken ct);
}
