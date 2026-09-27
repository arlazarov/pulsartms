namespace Application.Features.Dispatch.Interfaces;

public interface IDispatchReadTickets
{
  // The serving carrier's next read ticket for a provider, committed on its
  // own before the pass reads the provider: tickets only grow, and no two
  // passes hold the same one (audit F21).
  Task<long> TakeAsync(string provider, CancellationToken ct);
}
