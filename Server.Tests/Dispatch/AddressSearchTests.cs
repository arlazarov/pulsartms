using Application.Features.Addresses;
using Application.Features.Addresses.Interfaces;
using Application.Features.Addresses.Models;
using Application.Interfaces;
using Domain.Entities.Border;
using Domain.Entities.Shipments;

namespace Server.Tests.Dispatch;

[Trait("Category", "Addresses")]
[Trait("Kind", "Integration")]
public sealed class AddressSearchTests
{
  [Fact]
  public async Task HistoryPrecedesProviderAndDoesNotExposeRestrictedParties()
  {
    await using var f = await StopCompletionFixture.CreateAsync();
    var stop = f.Load.Stops.First();
    stop.Name = "Known warehouse";
    stop.Address = "123 Main Street";
    stop.City = "Toronto";
    stop.Province = "ON";
    stop.Country = "Canada";
    f.Db.Shipments.Add(
      new Shipment
      {
        Id = Guid.NewGuid(),
        LoadId = f.Load.Id,
        Shipper = new()
        {
          AddressLine1 = stop.Address,
          City = stop.City,
          Region = stop.Province,
          Country = stop.Country,
          PostalCode = stop.ZipCode,
        },
      }
    );
    f.Db.BorderCrossings.Add(
      new BorderCrossing
      {
        Id = Guid.NewGuid(),
        CarrierAddress = new()
        {
          AddressLine1 = "999 Main Street",
          City = "Toronto",
          Country = "CA",
        },
      }
    );
    await f.Db.SaveChangesAsync();
    var provider = new Provider();
    var result = await Handler(f, provider)
      .Handle(
        new SuggestAddresses(new("Main Street", Guid.NewGuid())),
        default
      );
    Assert.True(result.Success);
    Assert.Equal(2, result.Response!.Items.Count);
    Assert.NotNull(result.Response.Items[0].SavedAddress);
    Assert.Equal("CA", result.Response.Items[0].SavedAddress!.Country);
    Assert.Equal("provider", result.Response.Items[1].Id);
    Assert.DoesNotContain(result.Response.Items, x => x.Label.Contains("999"));
    var admin = await Handler(f, provider, "Admin")
      .Handle(
        new SuggestAddresses(new("Main Street", Guid.NewGuid())),
        default
      );
    Assert.Contains(admin.Response!.Items, x => x.Label.Contains("999"));
    provider.Unavailable = true;
    var offline = await Handler(f, provider)
      .Handle(
        new SuggestAddresses(new("Known warehouse", Guid.NewGuid())),
        default
      );
    Assert.True(offline.Response!.ProviderUnavailable);
    Assert.NotNull(Assert.Single(offline.Response.Items).SavedAddress);
  }

  [Fact]
  public async Task RejectsUnauthorizedAndShortQueriesWithoutProviderCalls()
  {
    await using var f = await StopCompletionFixture.CreateAsync();
    var provider = new Provider();
    var denied = await Handler(f, provider, "Driver")
      .Handle(
        new SuggestAddresses(new("Main Street", Guid.NewGuid())),
        default
      );
    Assert.Equal(403, denied.StatusCode);
    var shortQuery = await Handler(f, provider)
      .Handle(new SuggestAddresses(new("Ma", Guid.NewGuid())), default);
    Assert.Empty(shortQuery.Response!.Items);
    Assert.Equal(0, provider.Calls);
  }

  private static AddressSearchHandler Handler(
    StopCompletionFixture f,
    Provider provider,
    string role = "Dispatch"
  ) => new(f.Db, new Caller(), new Roles(role), provider);

  private sealed class Provider : IAddressSuggestionsProvider
  {
    public int Calls { get; private set; }
    public bool Unavailable { get; set; }

    public Task<AddressSuggestions> SuggestAsync(
      string query,
      Guid session,
      CancellationToken ct
    )
    {
      Calls++;
      return Task.FromResult(
        Unavailable
          ? new AddressSuggestions([], true)
          : new AddressSuggestions([new("provider", "124 Main Street")])
      );
    }

    public Task<PostalAddress?> ResolveAsync(
      string id,
      Guid session,
      CancellationToken ct
    ) => Task.FromResult<PostalAddress?>(null);
  }

  private sealed class Caller : ICurrentUser
  {
    public bool IsAuthenticated => true;
    public string IdentityUserId => "operator";
  }

  private sealed class Roles(string role) : IUserRoleService
  {
    public Task<string?> GetAsync(string id, CancellationToken ct = default) =>
      Task.FromResult<string?>(role);

    public Task<Dictionary<Guid, string>> GetAsync(
      IReadOnlyCollection<Guid> ids,
      CancellationToken ct = default
    ) => throw new NotSupportedException();

    public Task SetAsync(
      string id,
      string value,
      CancellationToken ct = default
    ) => throw new NotSupportedException();
  }
}
