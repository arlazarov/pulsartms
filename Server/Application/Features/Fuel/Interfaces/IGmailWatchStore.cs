using Application.Features.Fuel.Models;

namespace Application.Features.Fuel.Interfaces;

public interface IGmailWatchStore
{
  Task<bool> AcquireAsync(string owner, DateTime now, CancellationToken ct);
  Task<GmailWatchState?> ReadAsync(CancellationToken ct);
  Task SaveAsync(string owner, GmailWatchState state, CancellationToken ct);
  Task ReleaseAsync(string owner, CancellationToken ct);
}
