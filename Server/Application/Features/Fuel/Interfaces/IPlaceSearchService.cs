using Application.Features.Fuel.Models;

namespace Application.Features.Fuel.Interfaces;

public interface IPlaceSearchService
{
  Task<PlaceSearchResult?> SearchAsync(string query, CancellationToken cancellationToken = default);
}
