namespace Application.Interfaces;

// The carriers a background pass has to go round.
//
// Work that is driven by a queue takes the carrier from the row it
// claimed. Work that is driven by the clock - refreshing every truck's
// hours, pulling the fleet from the telematics provider - has no row to
// take it from, so it goes round the carriers one at a time and runs the
// pass as each of them in turn.
//
// One pass per carrier rather than one pass over everything: a single
// query across all of them would need the filters waved off, and the
// moment that is allowed anywhere it becomes the thing someone copies.
public interface ICompanyRoster
{
  Task<IReadOnlyList<Guid>> ActiveAsync(CancellationToken ct);
}
