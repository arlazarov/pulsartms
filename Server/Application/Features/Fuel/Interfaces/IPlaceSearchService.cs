using Application.Features.Fuel.Models;

namespace Application.Features.Fuel.Interfaces;

public interface IPlaceSearchService
{
  Task<PlaceSearchResult?> SearchAsync(
    string query,
    CancellationToken cancellationToken = default
  );

  // Re-reading a place already identified. Searching by text again would ask
  // the provider to guess the same answer twice, and a guess can land on a
  // different business next door - which would replace one station's status
  // with another's. An id cannot drift.
  Task<PlaceSearchResult?> ReadAsync(
    string placeId,
    CancellationToken cancellationToken = default
  );
}
