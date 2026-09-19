using Application.Features.Integrations.Models;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;

namespace Server.Tests.Identity;

[Trait("Category", "Identity")]
[Trait("Kind", "Integration")]
public sealed class IntegrationCredentialStoreTests
{
  private static IntegrationCredentialValues Key(string value) =>
    new(new Dictionary<string, string> { ["apiKey"] = value });

  [Fact]
  public async Task EmptyReadsDoNotCreateSettingsOrSaveAnything()
  {
    await using var fixture = await IntegrationCredentialFixture.CreateAsync();
    var writes = fixture.SaveCount;
    foreach (var provider in IntegrationProviderCatalog.Providers)
    {
      var read = await fixture.Store.ReadAsync(provider, default);
      Assert.Equal(0, read.Revision);
      Assert.Null(read.UpdatedAt);
      Assert.Null(read.Values);
    }
    await fixture.WithDbAsync(async db =>
      Assert.Empty(await db.IntegrationCredentialSettings.ToListAsync())
    );
    Assert.Equal(writes, fixture.SaveCount);
  }

  [Theory]
  [InlineData(IntegrationProviderCatalog.Torque)]
  [InlineData(IntegrationProviderCatalog.Samsara)]
  [InlineData(IntegrationProviderCatalog.GoogleEmail)]
  public async Task CompleteBundlesAreEncryptedAndSurviveProviderReplacement(
    string provider
  )
  {
    await using var fixture = await IntegrationCredentialFixture.CreateAsync();
    var fields = IntegrationProviderCatalog
      .Fields(provider)
      .ToDictionary(
        field => field,
        field => $"fixture-{provider}-{field}-not-a-real-credential"
      );
    Assert.True(
      await fixture.Store.TryWriteAsync(provider, 0, new(fields), default)
    );
    await fixture.WithDbAsync(async db =>
    {
      var row = Assert.Single(
        await db.IntegrationCredentialSettings.ToListAsync()
      );
      Assert.Equal(provider, row.Provider);
      Assert.Equal(1, row.Revision);
      Assert.NotNull(row.ProtectedValues);
      foreach (var value in fields.Values)
        Assert.DoesNotContain(value, row.ProtectedValues);
      Assert.DoesNotContain("clientSecret", row.ProtectedValues);
      Assert.NotEqual(default, row.UpdatedAt);
    });
    await fixture.ReplaceServiceProviderAsync();
    var writes = fixture.SaveCount;
    var read = await fixture.Store.ReadAsync(provider, default);
    foreach (var field in fields)
      Assert.Equal(field.Value, read.Values!.Get(field.Key));
    Assert.Equal("[Integration credentials]", read.Values!.ToString());
    Assert.Equal(writes, fixture.SaveCount);
  }

  [Fact]
  public async Task ConcurrentFirstInsertsHaveExactlyOneWinner()
  {
    await using var fixture = await IntegrationCredentialFixture.CreateAsync();
    fixture.HoldConcurrentCredentialWrites();
    var results = await Task.WhenAll(
      fixture.Store.TryWriteAsync(
        IntegrationProviderCatalog.Samsara,
        0,
        Key("fixture-first"),
        default
      ),
      fixture.Store.TryWriteAsync(
        IntegrationProviderCatalog.Samsara,
        0,
        Key("fixture-second"),
        default
      )
    );
    Assert.Single(results, result => result);
    var row = await fixture.Store.ReadAsync(
      IntegrationProviderCatalog.Samsara,
      default
    );
    Assert.Equal(1, row.Revision);
    Assert.Equal(
      results[0] ? "fixture-first" : "fixture-second",
      row.Values!.Get("apiKey")
    );
    await fixture.WithDbAsync(async db =>
    {
      Assert.Single(await db.IntegrationCredentialSettings.ToListAsync());
    });
  }

  [Fact]
  public async Task ConcurrentUpdatesUseIndependentContextsAndRejectStaleRevision()
  {
    await using var fixture = await IntegrationCredentialFixture.CreateAsync();
    Assert.True(
      await fixture.Store.TryWriteAsync(
        IntegrationProviderCatalog.Torque,
        0,
        Key("fixture-original"),
        default
      )
    );
    fixture.HoldConcurrentCredentialWrites();
    var results = await Task.WhenAll(
      fixture.Store.TryWriteAsync(
        IntegrationProviderCatalog.Torque,
        1,
        Key("fixture-first"),
        default
      ),
      fixture.Store.TryWriteAsync(
        IntegrationProviderCatalog.Torque,
        1,
        Key("fixture-second"),
        default
      )
    );
    Assert.Single(results, result => result);
    var row = await fixture.Store.ReadAsync(
      IntegrationProviderCatalog.Torque,
      default
    );
    Assert.Equal(2, row.Revision);
    Assert.Equal(
      results[0] ? "fixture-first" : "fixture-second",
      row.Values!.Get("apiKey")
    );
  }

  [Fact]
  public async Task RestoreRetainsTombstoneAndNeverReusesAnOldRevision()
  {
    await using var fixture = await IntegrationCredentialFixture.CreateAsync();
    var provider = IntegrationProviderCatalog.Samsara;
    Assert.True(
      await fixture.Store.TryWriteAsync(
        provider,
        0,
        Key("fixture-original"),
        default
      )
    );
    Assert.True(await fixture.Store.TryWriteAsync(provider, 1, null, default));
    var restored = await fixture.Store.ReadAsync(provider, default);
    Assert.Equal(2, restored.Revision);
    Assert.Null(restored.Values);
    Assert.NotNull(restored.UpdatedAt);
    Assert.False(
      await fixture.Store.TryWriteAsync(
        provider,
        0,
        Key("fixture-stale-insert"),
        default
      )
    );
    Assert.False(
      await fixture.Store.TryWriteAsync(
        provider,
        1,
        Key("fixture-stale-update"),
        default
      )
    );
    Assert.True(await fixture.Store.TryWriteAsync(provider, 2, null, default));
    Assert.True(
      await fixture.Store.TryWriteAsync(
        provider,
        3,
        Key("fixture-new"),
        default
      )
    );
    Assert.Equal(
      4,
      (await fixture.Store.ReadAsync(provider, default)).Revision
    );
    await fixture.WithDbAsync(async db =>
    {
      Assert.Single(await db.IntegrationCredentialSettings.ToListAsync());
    });
  }

  [Fact]
  public async Task FirstRestoreAlsoPreventsAStaleFirstSave()
  {
    await using var fixture = await IntegrationCredentialFixture.CreateAsync();
    Assert.True(
      await fixture.Store.TryWriteAsync(
        IntegrationProviderCatalog.Torque,
        0,
        null,
        default
      )
    );
    Assert.False(
      await fixture.Store.TryWriteAsync(
        IntegrationProviderCatalog.Torque,
        0,
        Key("fixture-stale"),
        default
      )
    );
    Assert.Equal(
      1,
      (
        await fixture.Store.ReadAsync(
          IntegrationProviderCatalog.Torque,
          default
        )
      ).Revision
    );
  }

  [Theory]
  [InlineData("google-maps")]
  [InlineData("Samsara")]
  [InlineData("")]
  public async Task UnsupportedProvidersCannotReadOrWrite(string provider)
  {
    await using var fixture = await IntegrationCredentialFixture.CreateAsync();
    var writes = fixture.SaveCount;
    await Assert.ThrowsAsync<ArgumentException>(
      () => fixture.Store.ReadAsync(provider, default)
    );
    await Assert.ThrowsAsync<ArgumentException>(
      () => fixture.Store.TryWriteAsync(provider, 0, Key("fixture"), default)
    );
    Assert.Equal(writes, fixture.SaveCount);
  }

  [Fact]
  public async Task UnknownMissingAndUnsafeFieldsCannotBeSaved()
  {
    await using var fixture = await IntegrationCredentialFixture.CreateAsync();
    var writes = fixture.SaveCount;
    foreach (
      var invalid in new[]
      {
        new IntegrationCredentialValues(
          new Dictionary<string, string>
          {
            ["apiKey"] = "fixture",
            ["unexpected"] = "fixture",
          }
        ),
        new IntegrationCredentialValues(
          Array.Empty<KeyValuePair<string, string>>()
        ),
        Key(" "),
        Key("fixture\nheader"),
        Key(new string('x', 8_193)),
      }
    )
      await Assert.ThrowsAsync<ArgumentException>(
        () =>
          fixture.Store.TryWriteAsync(
            IntegrationProviderCatalog.Samsara,
            0,
            invalid,
            default
          )
      );
    await Assert.ThrowsAsync<ArgumentException>(
      () =>
        fixture.Store.TryWriteAsync(
          IntegrationProviderCatalog.GoogleEmail,
          0,
          Key("fixture"),
          default
        )
    );
    Assert.Equal(writes, fixture.SaveCount);
  }

  [Fact]
  public async Task CiphertextCannotBeMovedBetweenProviders()
  {
    await using var fixture = await IntegrationCredentialFixture.CreateAsync();
    Assert.True(
      await fixture.Store.TryWriteAsync(
        IntegrationProviderCatalog.Torque,
        0,
        Key("fixture-torque"),
        default
      )
    );
    Assert.True(
      await fixture.Store.TryWriteAsync(
        IntegrationProviderCatalog.Samsara,
        0,
        Key("fixture-samsara"),
        default
      )
    );
    await fixture.WithDbAsync(async db =>
    {
      var torque = await db.IntegrationCredentialSettings.SingleAsync(row =>
        row.Provider == IntegrationProviderCatalog.Torque
      );
      var samsara = await db.IntegrationCredentialSettings.SingleAsync(row =>
        row.Provider == IntegrationProviderCatalog.Samsara
      );
      samsara.ProtectedValues = torque.ProtectedValues;
      await db.SaveChangesAsync();
    });
    var failure = await Assert.ThrowsAsync<InvalidOperationException>(
      () => fixture.Store.ReadAsync(IntegrationProviderCatalog.Samsara, default)
    );
    Assert.Equal(
      "Saved integration credentials are unavailable.",
      failure.Message
    );
    Assert.Null(failure.InnerException);
    Assert.Equal(
      "fixture-torque",
      (
        await fixture.Store.ReadAsync(
          IntegrationProviderCatalog.Torque,
          default
        )
      ).Values!.Get("apiKey")
    );
  }

  [Theory]
  [InlineData("not-cipher", false)]
  [InlineData("not-json", true)]
  [InlineData("{}", true)]
  [InlineData("{\"apiKey\":\"fixture\",\"unexpected\":\"fixture\"}", true)]
  [InlineData("{\"apiKey\":\"first\",\"apiKey\":\"second\"}", true)]
  [InlineData("{\"apiKey\":null}", true)]
  [InlineData("{\"apiKey\":\" \"}", true)]
  public async Task CorruptOrMalformedSavedBundlesFailClosedWithoutWriting(
    string content,
    bool encrypt
  )
  {
    await using var fixture = await IntegrationCredentialFixture.CreateAsync();
    var provider = IntegrationProviderCatalog.Samsara;
    var cipher = encrypt
      ? fixture
        .Protection.CreateProtector(
          "AMFTMS.IntegrationCredentials.v1",
          provider
        )
        .Protect(content)
      : content;
    await fixture.WithDbAsync(async db =>
    {
      db.IntegrationCredentialSettings.Add(
        new()
        {
          Provider = provider,
          ProtectedValues = cipher,
          Revision = 1,
          UpdatedAt = DateTime.UtcNow,
        }
      );
      await db.SaveChangesAsync();
    });
    var writes = fixture.SaveCount;
    var failure = await Assert.ThrowsAsync<InvalidOperationException>(
      () => fixture.Store.ReadAsync(provider, default)
    );
    Assert.Equal(
      "Saved integration credentials are unavailable.",
      failure.Message
    );
    Assert.Null(failure.InnerException);
    Assert.Equal(writes, fixture.SaveCount);
  }
}
