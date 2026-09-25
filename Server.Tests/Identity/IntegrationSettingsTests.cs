using System.Text.Json;
using Application.Features.Integrations.Commands;
using Application.Features.Integrations.Interfaces;
using Application.Features.Integrations.Models;
using Application.Features.Integrations.Queries;
using Application.Features.Integrations.Services;
using Application.Interfaces;

namespace Server.Tests.Identity;

[Trait("Category", "Identity")]
[Trait("Kind", "Unit")]
public sealed class IntegrationSettingsTests
{
  [Fact]
  public async Task ExistingConfigurationIsUsedWithoutCopyingOrChangingAnyKeys()
  {
    var fixture = new Fixture();
    // WhatsApp has no server configuration: it is not configured until an
    // administrator saves it.
    Assert.False(
      (await fixture.Service.GetStateAsync("whatsapp", default)).Configured
    );
    foreach (
      var provider in IntegrationProviderCatalog.Providers.Where(x =>
        x != "whatsapp"
      )
    )
    {
      var initial = await fixture.Service.GetStateAsync(provider, default);
      Assert.True(initial.Configured);
      Assert.False(initial.UsesSavedSettings);
      Assert.False(initial.CanRestoreDeployment);
      Assert.Equal(0, initial.Revision);
      var effective = await fixture.Service.GetAsync(provider, default);
      Assert.Same(fixture.Deployment.Get(provider), effective);
    }
    Assert.Empty(fixture.Store.Saved);
    Assert.Equal(0, fixture.Store.Writes);
  }

  [Theory]
  [InlineData(null)]
  [InlineData("")]
  [InlineData("   ")]
  public async Task BlankFieldsLeaveBothDeploymentAndSavedKeysUntouched(
    string? blank
  )
  {
    var fixture = new Fixture();
    var defaults = fixture.Deployment.Get("samsara");
    var unchanged = await fixture.Service.SaveAsync(
      "samsara",
      new() { Fields = new() { ["apiKey"] = blank } },
      default
    );
    Assert.True(unchanged.Success);
    Assert.Equal(0, fixture.Store.Writes);
    Assert.Same(defaults, await fixture.Service.GetAsync("samsara", default));
    var saved = await fixture.Service.SaveAsync(
      "samsara",
      Update("apiKey", "replacement-key"),
      default
    );
    var stillSaved = await fixture.Service.SaveAsync(
      "samsara",
      new()
      {
        Revision = saved.Response!.Revision,
        Fields = new() { ["apiKey"] = blank },
      },
      default
    );
    Assert.True(stillSaved.Success);
    Assert.Equal(saved.Response.Revision, stillSaved.Response!.Revision);
    Assert.Equal(
      "replacement-key",
      (await fixture.Service.GetAsync("samsara", default)).Get("apiKey")
    );
    Assert.Equal("samsara-deployment-secret", defaults.Get("apiKey"));
    Assert.Equal(1, fixture.Store.Writes);
  }

  [Fact]
  public async Task ProviderUpdatesAreIndependentAndDoNotReturnSecrets()
  {
    var fixture = new Fixture();
    var saved = await fixture.Service.SaveAsync(
      "torqueai",
      Update("apiKey", "sensitive-new-torque-value"),
      default
    );
    Assert.True(saved.Success);
    Assert.True(saved.Response!.UsesSavedSettings);
    Assert.True(saved.Response.CanRestoreDeployment);
    Assert.Equal(
      "sensitive-new-torque-value",
      (await fixture.Service.GetAsync("torqueai", default)).Get("apiKey")
    );
    Assert.Equal(
      "samsara-deployment-secret",
      (await fixture.Service.GetAsync("samsara", default)).Get("apiKey")
    );
    Assert.Equal(
      "mail-deployment-refresh",
      (await fixture.Service.GetAsync("google-email", default)).Get(
        "refreshToken"
      )
    );
    var handler = new GetIntegrationSettingsHandler(
      fixture.Service,
      new Caller(),
      new Roles("Admin")
    );
    var states = await handler.Handle(new(), default);
    var json = JsonSerializer.Serialize(states);
    Assert.DoesNotContain("sensitive-new-torque-value", json);
    Assert.DoesNotContain("deployment-secret", json);
    Assert.DoesNotContain("mail-deployment", json);
    Assert.DoesNotContain(
      "sensitive-new-torque-value",
      (await fixture.Service.GetAsync("torqueai", default)).ToString()
    );
    Assert.DoesNotContain(
      "sensitive-new-torque-value",
      JsonSerializer.Serialize(
        await fixture.Service.GetAsync("torqueai", default)
      )
    );
  }

  [Fact]
  public async Task RestoreUsesRetainedServerConfigurationAndDoesNotResetRevision()
  {
    var fixture = new Fixture();
    var saved = await fixture.Service.SaveAsync(
      "samsara",
      Update("apiKey", "replacement-key"),
      default
    );
    var restored = await fixture.Service.SaveAsync(
      "samsara",
      new() { Revision = saved.Response!.Revision, RestoreDeployment = true },
      default
    );
    Assert.True(restored.Success);
    Assert.False(restored.Response!.UsesSavedSettings);
    Assert.Equal(2, restored.Response.Revision);
    Assert.Null(fixture.Store.Saved["samsara"].Values);
    Assert.Equal(
      "samsara-deployment-secret",
      (await fixture.Service.GetAsync("samsara", default)).Get("apiKey")
    );
    var stale = await fixture.Service.SaveAsync(
      "samsara",
      Update("apiKey", "stale-key"),
      default
    );
    Assert.False(stale.Success);
    Assert.Equal(409, stale.StatusCode);
    Assert.Equal(2, fixture.Store.Writes);
  }

  [Fact]
  public async Task ExplicitRestoreBeforeFirstSaveInvalidatesAnyPendingInitialSave()
  {
    var fixture = new Fixture();
    var restored = await fixture.Service.SaveAsync(
      "samsara",
      new() { RestoreDeployment = true },
      default
    );
    Assert.True(restored.Success);
    Assert.Equal(1, restored.Response!.Revision);
    Assert.False(restored.Response.UsesSavedSettings);
    var stale = await fixture.Service.SaveAsync(
      "samsara",
      Update("apiKey", "stale-initial-value"),
      default
    );
    Assert.Equal(409, stale.StatusCode);
    Assert.Equal(
      "samsara-deployment-secret",
      (await fixture.Service.GetAsync("samsara", default)).Get("apiKey")
    );
  }

  [Fact]
  public async Task RestoreCannotDiscardSavedCredentialsWhenDeploymentIsIncomplete()
  {
    var fixture = new Fixture();
    await fixture.Service.SaveAsync(
      "samsara",
      Update("apiKey", "replacement-key"),
      default
    );
    fixture.Deployment.Values["samsara"] = new([]);
    var state = await fixture.Service.GetStateAsync("samsara", default);
    Assert.False(state.CanRestoreDeployment);
    var rejected = await fixture.Service.SaveAsync(
      "samsara",
      new() { Revision = 1, RestoreDeployment = true },
      default
    );
    Assert.False(rejected.Success);
    Assert.Equal(
      "replacement-key",
      (await fixture.Service.GetAsync("samsara", default)).Get("apiKey")
    );
  }

  [Fact]
  public async Task EmailTokenRotationPreservesOneCompleteTupleAcrossLaterConfigurationChanges()
  {
    var fixture = new Fixture();
    var saved = await fixture.Service.SaveAsync(
      "google-email",
      Update("refreshToken", "new-refresh"),
      default
    );
    Assert.True(saved.Success);
    fixture.Deployment.Values["google-email"] = Values(
      ("clientId", "different-id"),
      ("clientSecret", "different-secret"),
      ("refreshToken", "different-refresh")
    );
    var credentials = await fixture.Service.GetAsync("google-email", default);
    Assert.Equal("mail-deployment-id", credentials.Get("clientId"));
    Assert.Equal("mail-deployment-secret", credentials.Get("clientSecret"));
    Assert.Equal("new-refresh", credentials.Get("refreshToken"));
  }

  [Theory]
  [InlineData("clientId")]
  [InlineData("clientSecret")]
  [InlineData("refreshToken")]
  public async Task InitiallyMissingEmailConfigurationRequiresAllFields(
    string field
  )
  {
    var fixture = new Fixture();
    fixture.Deployment.Values["google-email"] = new([]);
    var rejected = await fixture.Service.SaveAsync(
      "google-email",
      Update(field, "single-value"),
      default
    );
    Assert.False(rejected.Success);
    Assert.Empty(fixture.Store.Saved);
  }

  [Fact]
  public async Task DifferentGoogleClientIdRequiresMatchingSecretAndTokenInSameSave()
  {
    var fixture = new Fixture();
    var rejected = await fixture.Service.SaveAsync(
      "google-email",
      Update("clientId", "different-id"),
      default
    );
    Assert.False(rejected.Success);
    Assert.Equal(0, fixture.Store.Writes);
    var accepted = await fixture.Service.SaveAsync(
      "google-email",
      new()
      {
        Fields = new()
        {
          ["clientId"] = "different-id",
          ["clientSecret"] = "matching-secret",
          ["refreshToken"] = "matching-refresh",
        },
      },
      default
    );
    Assert.True(accepted.Success);
    Assert.Equal(
      "different-id",
      (await fixture.Service.GetAsync("google-email", default)).Get("clientId")
    );
  }

  [Fact]
  public async Task CorruptSavedCredentialsDoNotSilentlyRevertToOldConfiguration()
  {
    var fixture = new Fixture();
    fixture.Store.ReadFailure = true;
    await Assert.ThrowsAsync<InvalidOperationException>(
      () => fixture.Service.GetAsync("samsara", default)
    );
    await Assert.ThrowsAsync<InvalidOperationException>(
      () => fixture.Service.GetStateAsync("samsara", default)
    );
    Assert.Equal(0, fixture.Store.Writes);
  }

  [Fact]
  public async Task StoreConflictIsReturnedWithoutOverwritingWinningCredentials()
  {
    var fixture = new Fixture();
    fixture.Store.RejectWrite = true;
    var result = await fixture.Service.SaveAsync(
      "samsara",
      Update("apiKey", "losing-secret"),
      default
    );
    Assert.Equal(409, result.StatusCode);
    Assert.DoesNotContain("losing-secret", JsonSerializer.Serialize(result));
    Assert.Empty(fixture.Store.Saved);
  }

  [Theory]
  [InlineData(false, "Admin", 401)]
  [InlineData(true, "Dispatch", 403)]
  [InlineData(true, null, 403)]
  public async Task UnauthorizedOrInactiveCallersCannotReadOrWriteCredentials(
    bool authenticated,
    string? role,
    int status
  )
  {
    var fixture = new Fixture();
    var caller = new Caller { IsAuthenticated = authenticated };
    var roles = new Roles(role);
    var get = await new GetIntegrationSettingsHandler(
      fixture.Service,
      caller,
      roles
    ).Handle(new(), default);
    var put = await new UpdateIntegrationCredentialsHandler(
      fixture.Service,
      caller,
      roles
    ).Handle(new("samsara", Update("apiKey", "never-saved")), default);
    Assert.Equal(status, get.StatusCode);
    Assert.Equal(status, put.StatusCode);
    Assert.Equal(0, fixture.Store.Reads);
    Assert.Equal(0, fixture.Store.Writes);
  }

  [Theory]
  [InlineData("tomtom", "apiKey", "secret", 0)]
  [InlineData("google-maps", "apiKey", "secret", 0)]
  [InlineData("samsara", "refreshToken", "secret", 0)]
  [InlineData("samsara", "apiKey", "has space", 0)]
  [InlineData("samsara", "apiKey", "has\nline", 0)]
  [InlineData("samsara", "apiKey", "secret", -1)]
  [InlineData("samsara", "apiKey", "secret", long.MaxValue)]
  public void InvalidCredentialsHaveOnlyNonSecretValidationMessages(
    string provider,
    string field,
    string value,
    long revision
  )
  {
    var command = new UpdateIntegrationCredentialsCommand(
      provider,
      new()
      {
        Revision = revision,
        Fields = new() { [field] = value },
      }
    );
    var wrong = command.Wrong().ToArray();
    Assert.NotEmpty(wrong);
    Assert.DoesNotContain(value, string.Join(" ", wrong));
  }

  // Every field a provider has may come in one save - WhatsApp's four as
  // much as a single API key - with blank ones counted by key; a field the
  // provider does not have is refused, even beside a complete set.
  [Theory]
  [InlineData("torqueai", "")]
  [InlineData("samsara", "")]
  [InlineData("google-email", "")]
  [InlineData("whatsapp", "")]
  [InlineData("whatsapp", "appSecret")]
  public void AProvidersOwnFieldsAreAcceptedTogether(
    string provider,
    string blank
  )
  {
    var fields = IntegrationProviderCatalog
      .Fields(provider)
      .ToDictionary(
        field => field,
        field => (string?)(field == blank ? "" : $"{field}-value")
      );

    Assert.Empty(
      new UpdateIntegrationCredentialsCommand(
        provider,
        new() { Fields = fields }
      ).Wrong()
    );
    fields["notAField"] = "unknown-value";
    Assert.NotEmpty(
      new UpdateIntegrationCredentialsCommand(
        provider,
        new() { Fields = fields }
      ).Wrong()
    );
  }

  [Fact]
  public async Task AllFourWhatsAppFieldsAreSavedInOneWrite()
  {
    var fixture = new Fixture();
    var fields = IntegrationProviderCatalog
      .Fields("whatsapp")
      .ToDictionary(field => field, field => (string?)$"{field}-value");
    var command = new UpdateIntegrationCredentialsCommand(
      "whatsapp",
      new() { Fields = fields }
    );
    Assert.Empty(command.Wrong());

    var saved = await fixture.Service.SaveAsync(
      "whatsapp",
      command.Update,
      default
    );

    Assert.True(saved.Success);
    Assert.Equal(1, fixture.Store.Writes);
    Assert.True(saved.Response!.Configured);
    var stored = await fixture.Service.GetAsync("whatsapp", default);
    Assert.All(
      IntegrationProviderCatalog.Fields("whatsapp"),
      field => Assert.Equal($"{field}-value", stored.Get(field))
    );
  }

  // A WhatsApp number another company has saved is not taken silently:
  // its webhooks reach only one company. A new token for the number this
  // company already holds is not a new claim and still saves, and the
  // state says the number is shared.
  [Fact]
  public async Task ANumberAnotherCompanyHoldsIsRefusedButOwnOneKeeps()
  {
    var fixture = new Fixture();
    fixture.Store.Elsewhere.Add(("whatsapp", "phoneNumberId", "shared"));
    Dictionary<string, string?> Bundle(string number, string token) =>
      new()
      {
        ["phoneNumberId"] = number,
        ["accessToken"] = token,
        ["appSecret"] = "secret",
        ["verifyToken"] = "verify",
      };

    var taken = await fixture.Service.SaveAsync(
      "whatsapp",
      new() { Fields = Bundle("shared", "one") },
      default
    );
    Assert.Equal(422, taken.StatusCode);
    Assert.Equal(0, fixture.Store.Writes);

    var own = await fixture.Service.SaveAsync(
      "whatsapp",
      new() { Fields = Bundle("own", "one") },
      default
    );
    Assert.True(own.Success);
    Assert.False(own.Response!.HeldElsewhere);

    // Already held here and, since, by another company too (as production
    // was on September 25): a new token still saves, and it is flagged.
    fixture.Store.Elsewhere.Add(("whatsapp", "phoneNumberId", "own"));
    var rotated = await fixture.Service.SaveAsync(
      "whatsapp",
      new() { Revision = own.Response.Revision, Fields = Bundle("", "two") },
      default
    );
    Assert.True(rotated.Success);
    Assert.True(rotated.Response!.HeldElsewhere);
    Assert.True(
      (await fixture.Service.GetStateAsync("whatsapp", default)).HeldElsewhere
    );
    Assert.Equal(
      "two",
      (await fixture.Service.GetAsync("whatsapp", default)).Get("accessToken")
    );
  }

  [Fact]
  public void NullOversizeAndRestoreWithReplacementRequestsAreRejected()
  {
    Assert.NotEmpty(
      new UpdateIntegrationCredentialsCommand("samsara", null!).Wrong()
    );
    Assert.NotEmpty(
      new UpdateIntegrationCredentialsCommand(
        "samsara",
        new() { Fields = null }
      ).Wrong()
    );
    Assert.NotEmpty(
      new UpdateIntegrationCredentialsCommand(
        "samsara",
        Update("apiKey", new('x', 4097))
      ).Wrong()
    );
    Assert.NotEmpty(
      new UpdateIntegrationCredentialsCommand(
        "samsara",
        new()
        {
          RestoreDeployment = true,
          Fields = new() { ["apiKey"] = "replacement" },
        }
      ).Wrong()
    );
    Assert.Empty(
      new UpdateIntegrationCredentialsCommand(
        "samsara",
        Update("apiKey", "  replacement  ")
      ).Wrong()
    );
  }

  private static IntegrationCredentialsUpdate Update(
    string field,
    string value
  ) => new() { Fields = new() { [field] = value } };

  private static IntegrationCredentialValues Values(
    params (string Name, string Value)[] fields
  ) =>
    new(
      fields.Select(field => new KeyValuePair<string, string>(
        field.Name,
        field.Value
      ))
    );

  private sealed class Fixture
  {
    public Store Store { get; } = new();
    public Deployment Deployment { get; } = new();
    public IntegrationSettingsService Service { get; }

    public Fixture() => Service = new(Store, Deployment);
  }

  private sealed class Deployment : IIntegrationDeploymentCredentials
  {
    public Dictionary<string, IntegrationCredentialValues> Values { get; } =
      new()
      {
        ["torqueai"] = IntegrationSettingsTests.Values(
          ("apiKey", "torque-deployment-secret")
        ),
        ["samsara"] = IntegrationSettingsTests.Values(
          ("apiKey", "samsara-deployment-secret")
        ),
        ["google-email"] = IntegrationSettingsTests.Values(
          ("clientId", "mail-deployment-id"),
          ("clientSecret", "mail-deployment-secret"),
          ("refreshToken", "mail-deployment-refresh")
        ),
        // WhatsApp has no server configuration.
        ["whatsapp"] = IntegrationSettingsTests.Values(),
      };

    public IntegrationCredentialValues Get(string provider) => Values[provider];
  }

  private sealed class Store : IIntegrationCredentialStore
  {
    public Dictionary<string, StoredIntegrationCredentials> Saved { get; } =
      new();
    public int Writes { get; private set; }
    public int Reads { get; private set; }
    public bool ReadFailure { get; set; }
    public bool RejectWrite { get; set; }

    public Task<StoredIntegrationCredentials> ReadAsync(
      string provider,
      CancellationToken ct
    )
    {
      ct.ThrowIfCancellationRequested();
      Reads++;
      if (ReadFailure)
        throw new InvalidOperationException(
          "Stored integration credentials are unavailable."
        );
      return Task.FromResult(
        Saved.GetValueOrDefault(provider) ?? new(0, null, null)
      );
    }

    public Task<bool> TryWriteAsync(
      string provider,
      long expectedRevision,
      IntegrationCredentialValues? values,
      CancellationToken ct
    )
    {
      ct.ThrowIfCancellationRequested();
      if (
        RejectWrite
        || expectedRevision
          != (Saved.GetValueOrDefault(provider)?.Revision ?? 0)
      )
        return Task.FromResult(false);
      Saved[provider] = new(expectedRevision + 1, DateTime.UtcNow, values);
      Writes++;
      return Task.FromResult(true);
    }

    // Channels other companies hold, as (provider, field, value).
    public HashSet<(string, string, string)> Elsewhere { get; } = [];

    public Task<bool> HeldElsewhereAsync(
      string provider,
      string field,
      string value,
      CancellationToken ct
    ) => Task.FromResult(Elsewhere.Contains((provider, field, value)));
  }

  private sealed class Caller : ICurrentUser
  {
    public bool IsAuthenticated { get; init; } = true;
    public string? IdentityUserId => IsAuthenticated ? "test-admin" : null;
  }

  private sealed class Roles(string? role) : IUserRoleService
  {
    public Task<string?> GetAsync(
      string identityId,
      CancellationToken ct = default
    ) => Task.FromResult(role);

    public Task<Dictionary<Guid, string>> GetAsync(
      IReadOnlyCollection<Guid> ids,
      CancellationToken ct = default
    ) => throw new NotSupportedException();

    public Task SetAsync(
      string identityId,
      string value,
      CancellationToken ct = default
    ) => throw new NotSupportedException();
  }
}
