using Application.Features.Integrations.Models;
using Infrastructure.Integrations;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Configuration;

namespace Server.Tests.Identity;

[Trait("Category", "Identity")]
[Trait("Kind", "Integration")]
public sealed class CompanyCredentialTests
{
  [Fact]
  public async Task OriginalCarrierCanReadLegacyProtectedBundles()
  {
    await using var f = await IntegrationCredentialFixture.CreateAsync();
    var provider = IntegrationProviderCatalog.Torque;
    var legacy = f
      .Protection.CreateProtector("AMFTMS.IntegrationCredentials.v1", provider)
      .Protect("{\"apiKey\":\"original\"}");
    await f.WithDbAsync(async db =>
    {
      db.IntegrationCredentialSettings.Add(
        new()
        {
          Provider = provider,
          ProtectedValues = legacy,
          Revision = 1,
          UpdatedAt = DateTime.UtcNow,
        }
      );
      await db.SaveChangesAsync();
    });
    Assert.Equal(
      "original",
      (await f.Store.ReadAsync(provider, default)).Values!.Get("apiKey")
    );
    using (f.Companies.As(Guid.NewGuid()))
      Assert.Null((await f.Store.ReadAsync(provider, default)).Values);
  }

  [Fact]
  public async Task CompaniesHaveIndependentBundlesAndRevisions()
  {
    await using var f = await IntegrationCredentialFixture.CreateAsync();
    var provider = IntegrationProviderCatalog.Torque;
    Assert.True(
      await f.Store.TryWriteAsync(
        provider,
        0,
        new(new Dictionary<string, string> { ["apiKey"] = "A" }),
        default
      )
    );
    using (f.Companies.As(Guid.NewGuid()))
    {
      var empty = await f.Store.ReadAsync(provider, default);
      Assert.Null(empty.Values);
      Assert.Equal(0, empty.Revision);
      Assert.True(
        await f.Store.TryWriteAsync(
          provider,
          0,
          new(new Dictionary<string, string> { ["apiKey"] = "B" }),
          default
        )
      );
      Assert.Equal(
        "B",
        (await f.Store.ReadAsync(provider, default)).Values!.Get("apiKey")
      );
    }
    Assert.Equal(
      "A",
      (await f.Store.ReadAsync(provider, default)).Values!.Get("apiKey")
    );
  }
}
