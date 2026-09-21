using Application.Features.Addresses.Interfaces;
using Application.Features.Addresses.Models;
using Application.Features.Execution.Models;
using Application.Models;

namespace Application.Features.Addresses;

public sealed record SuggestAddresses(SuggestAddressesRequest Request)
  : IRequest<RequestResponse<AddressSuggestions>>;

public sealed record ResolveSuggestedAddress(
  ResolveSuggestedAddressRequest Request
) : IRequest<RequestResponse<PostalAddress>>;

public sealed class AddressSearchHandler(
  IAppDbContext db,
  ICurrentUser caller,
  IUserRoleService roles,
  IAddressSuggestionsProvider provider
)
  : IRequestHandler<SuggestAddresses, RequestResponse<AddressSuggestions>>,
    IRequestHandler<ResolveSuggestedAddress, RequestResponse<PostalAddress>>
{
  public async Task<RequestResponse<AddressSuggestions>> Handle(
    SuggestAddresses command,
    CancellationToken ct
  )
  {
    if (await ExecutionCommandSupport.ActorAsync(db, caller, roles, ct) is null)
      return RequestResponse<AddressSuggestions>.Fail("Access denied.", 403);
    var request = command.Request;
    var query = request.Query?.Trim() ?? "";
    if (query.Length > 300 || request.Session == Guid.Empty)
      return RequestResponse<AddressSuggestions>.Fail(
        "Invalid address search."
      );
    if (query.Length < 3)
      return RequestResponse<AddressSuggestions>.Ok(new([]));
    var history = await HistoryAsync(query, ct);
    var suggestions = await provider.SuggestAsync(query, request.Session, ct);
    var known = history.Select(x => Key(x.Label)).ToHashSet();
    return RequestResponse<AddressSuggestions>.Ok(
      new(
        history
          .Concat(suggestions.Items.Where(x => known.Add(Key(x.Label))))
          .Take(10)
          .ToArray(),
        suggestions.ProviderUnavailable
      )
    );
  }

  public async Task<RequestResponse<PostalAddress>> Handle(
    ResolveSuggestedAddress command,
    CancellationToken ct
  )
  {
    if (await ExecutionCommandSupport.ActorAsync(db, caller, roles, ct) is null)
      return RequestResponse<PostalAddress>.Fail("Access denied.", 403);
    var request = command.Request;
    if (
      string.IsNullOrWhiteSpace(request.Id)
      || request.Id.Length > 300
      || request.Session == Guid.Empty
    )
      return RequestResponse<PostalAddress>.Fail("Invalid address selection.");
    var address = await provider.ResolveAsync(request.Id, request.Session, ct);
    return address is null
      ? RequestResponse<PostalAddress>.Fail(
        "Address details are unavailable. Try another suggestion or enter the address manually.",
        422
      )
      : RequestResponse<PostalAddress>.Ok(address);
  }

  private sealed class UsedAddress
  {
    public string Name { get; set; } = "";
    public string Address { get; set; } = "";
    public string City { get; set; } = "";
    public string Region { get; set; } = "";
    public string Country { get; set; } = "";
    public string PostalCode { get; set; } = "";
  }

  private async Task<List<AddressSuggestion>> HistoryAsync(
    string query,
    CancellationToken ct
  )
  {
    var rows = new List<UsedAddress>();
    await Add(
      db.DispatchStops.AsNoTracking()
        .Select(x => new UsedAddress
        {
          Name = x.Name,
          Address = x.Address,
          City = x.City,
          Region = x.Province,
          Country = x.Country,
          PostalCode = x.ZipCode,
        })
    );
    await Add(
      db.Shipments.AsNoTracking()
        .Select(x => new UsedAddress
        {
          Name = x.Shipper.Name,
          Address = x.Shipper.AddressLine1,
          City = x.Shipper.City,
          Region = x.Shipper.Region,
          Country = x.Shipper.Country,
          PostalCode = x.Shipper.PostalCode,
        })
    );
    await Add(
      db.Shipments.AsNoTracking()
        .Select(x => new UsedAddress
        {
          Name = x.Consignee.Name,
          Address = x.Consignee.AddressLine1,
          City = x.Consignee.City,
          Region = x.Consignee.Region,
          Country = x.Consignee.Country,
          PostalCode = x.Consignee.PostalCode,
        })
    );
    // Restricted customs parties must not leak through ordinary Dispatch reads.
    if (await roles.GetAsync(caller.IdentityUserId!, ct) == "Admin")
    {
      await Add(
        db.BorderCrossings.AsNoTracking()
          .Select(x => new UsedAddress
          {
            Name = x.CarrierAddress.Name,
            Address = x.CarrierAddress.AddressLine1,
            City = x.CarrierAddress.City,
            Region = x.CarrierAddress.Region,
            Country = x.CarrierAddress.Country,
            PostalCode = x.CarrierAddress.PostalCode,
          })
      );
      await Add(
        db.BorderCrossings.AsNoTracking()
          .SelectMany(x => x.Shipments)
          .Select(x => new UsedAddress
          {
            Name = x.Importer.Name,
            Address = x.Importer.AddressLine1,
            City = x.Importer.City,
            Region = x.Importer.Region,
            Country = x.Importer.Country,
            PostalCode = x.Importer.PostalCode,
          })
      );
      await Add(
        db.BorderCrossings.AsNoTracking()
          .SelectMany(x => x.Shipments)
          .Select(x => new UsedAddress
          {
            Name = x.CustomsBroker.Name,
            Address = x.CustomsBroker.AddressLine1,
            City = x.CustomsBroker.City,
            Region = x.CustomsBroker.Region,
            Country = x.CustomsBroker.Country,
            PostalCode = x.CustomsBroker.PostalCode,
          })
      );
    }
    return rows.GroupBy(x => Key(Label(x)))
      .Select(x => x.First())
      .OrderByDescending(x =>
        x.Address.StartsWith(query, StringComparison.OrdinalIgnoreCase)
      )
      .ThenBy(x => Label(x), StringComparer.OrdinalIgnoreCase)
      .Take(5)
      .Select(x => new AddressSuggestion(
        "",
        Label(x),
        new(x.Address, x.City, x.Region, CountryCode(x.Country), x.PostalCode)
      ))
      .ToList();

    async Task Add(IQueryable<UsedAddress> source)
    {
      source = source.Where(x => x.Address != "");
      foreach (
        var word in query
          .Split([' ', ','], StringSplitOptions.RemoveEmptyEntries)
          .Take(12)
      )
      {
        var term = word.ToUpperInvariant();
        source = source.Where(x =>
          (
            x.Name
            + " "
            + x.Address
            + " "
            + x.City
            + " "
            + x.Region
            + " "
            + x.Country
            + " "
            + x.PostalCode
          )
            .ToUpper()
            .Contains(term)
        );
      }
      rows.AddRange(
        await source
          .OrderBy(x => x.Address)
          .ThenBy(x => x.City)
          .Take(100)
          .ToListAsync(ct)
      );
    }
  }

  private static string CountryCode(string value) =>
    value.Trim().ToUpperInvariant() switch
    {
      "CA" or "CAN" or "CANADA" => "CA",
      "US" or "USA" or "UNITED STATES" or "UNITED STATES OF AMERICA" => "US",
      _ => value,
    };

  private static string Label(UsedAddress x) =>
    string.Join(
      ", ",
      new[] { x.Address, x.City, x.Region, x.PostalCode, x.Country }.Where(x =>
        !string.IsNullOrWhiteSpace(x)
      )
    );

  private static string Key(string value) =>
    string.Join(
      " ",
      value
        .ToUpperInvariant()
        .Split([' ', ','], StringSplitOptions.RemoveEmptyEntries)
    );
}
