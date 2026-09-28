namespace Application.Features.Dispatch.Interfaces;

public interface IDispatchReadTickets
{
  // The serving carrier's next read ticket for a provider, committed on its
  // own before the pass reads the provider: tickets only grow, and no two
  // passes hold the same one (audit F21). It comes with the count of passes
  // that have changed loads so far.
  Task<DispatchReadTicket> TakeAsync(string provider, CancellationToken ct);

  // Inside the pass's transaction, once it has changed loads: counts the
  // write, and answers the count this process has now seen.
  Task<long> WroteAsync(string provider, CancellationToken ct);
}

public sealed record DispatchReadTicket(long Ticket, long Written);
