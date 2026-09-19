using Application.Features.Addresses.Models;

namespace Application.Features.Addresses.Interfaces;

public interface IAddressSuggestionsProvider
{
  Task<AddressSuggestions> SuggestAsync(
    string query,
    Guid session,
    CancellationToken ct
  );
  Task<PostalAddress?> ResolveAsync(
    string id,
    Guid session,
    CancellationToken ct
  );
}
