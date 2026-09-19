namespace Application.Features.Addresses.Models;

public sealed record PostalAddress(
  string Address,
  string City,
  string Region,
  string Country,
  string PostalCode
);

public sealed record AddressSuggestion(
  string Id,
  string Label,
  PostalAddress? SavedAddress = null,
  string PostalCode = ""
);

public sealed record AddressSuggestions(
  IReadOnlyList<AddressSuggestion> Items,
  bool ProviderUnavailable = false
);

public sealed record SuggestAddressesRequest(string Query, Guid Session);

public sealed record ResolveSuggestedAddressRequest(string Id, Guid Session);
