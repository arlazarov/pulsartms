using Application.Features.Integrations.Interfaces;
using Application.Features.Integrations.Models;
using Application.Features.Integrations.Services;
using Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Server.Tests.Support;

namespace Server.Tests.Identity;

[Trait("Category", "Identity")]
[Trait("Kind", "Integration")]
public sealed class IntegrationChannelAdmissionTests
{
  [Fact]
  public async Task StaleOwnershipObservationsCannotAdmitTwoCompanies()
  {
    await using var fixture = await IntegrationCredentialFixture.CreateAsync();
    var other = Guid.NewGuid();
    await fixture.WithDbAsync(async db =>
    {
      db.Companies.Add(
        new Company
        {
          Id = other,
          Key = "other",
          Name = "Other",
        }
      );
      await db.SaveChangesAsync();
    });
    var barrier = new ReadBarrier(fixture.Store);
    var service = new IntegrationSettingsService(barrier, new NoDeployment());
    async Task<bool> Save(Guid company)
    {
      using var scope = fixture.Companies.As(company);
      var result = await service.SaveAsync(
        "whatsapp",
        new()
        {
          Revision = 0,
          Fields = new()
          {
            ["phoneNumberId"] = "shared-number",
            ["accessToken"] = "synthetic-token",
            ["appSecret"] = "synthetic-secret",
            ["verifyToken"] = "synthetic-verify",
          },
        },
        default
      );
      return result.Success;
    }
    var first = Save(Company.Amf);
    await barrier.FirstChecked.Task.WaitAsync(TimeSpan.FromSeconds(5));
    var second = Save(other);
    await barrier.SecondChecked.Task.WaitAsync(TimeSpan.FromSeconds(5));
    barrier.ReleaseFirst.SetResult();
    Assert.True(await first);
    barrier.ReleaseSecond.SetResult();
    Assert.False(await second);
    await fixture.WithDbAsync(async db =>
      Assert.Single(
        await db
          .IntegrationCredentialSettings.IgnoreQueryFilters()
          .ToListAsync()
      )
    );
  }

  [Fact]
  public async Task DirectStoreWriterCannotBypassClaimButMayClaimAfterRelease()
  {
    await using var fixture = await IntegrationCredentialFixture.CreateAsync();
    var other = Guid.NewGuid();
    await fixture.WithDbAsync(async db =>
    {
      db.Companies.Add(
        new Company
        {
          Id = other,
          Key = "other",
          Name = "Other",
        }
      );
      await db.SaveChangesAsync();
    });
    var values = new IntegrationCredentialValues(
      new Dictionary<string, string>
      {
        ["phoneNumberId"] = "number",
        ["accessToken"] = "token",
        ["appSecret"] = "secret",
        ["verifyToken"] = "verify",
      }
    );
    Assert.True(
      await fixture.Store.TryWriteAsync("whatsapp", 0, values, default)
    );
    using (fixture.Companies.As(other))
      await Assert.ThrowsAsync<IntegrationChannelConflictException>(
        () => fixture.Store.TryWriteAsync("whatsapp", 0, values, default)
      );
    Assert.True(
      await fixture.Store.TryWriteAsync("whatsapp", 1, null, default)
    );
    using (fixture.Companies.As(other))
      Assert.True(
        await fixture.Store.TryWriteAsync("whatsapp", 0, values, default)
      );
  }

  [Fact]
  public async Task AnUnreadableExistingClaimCannotAuthorizeAnotherCompany()
  {
    await using var f = await IntegrationCredentialFixture.CreateAsync();
    var other = Guid.NewGuid();
    await f.WithDbAsync(async db =>
    {
      db.Companies.Add(
        new Company
        {
          Id = other,
          Key = "unreadable",
          Name = "Unreadable",
        }
      );
      db.IntegrationCredentialSettings.Add(
        new IntegrationCredentialSetting
        {
          CompanyId = other,
          Provider = "whatsapp",
          Revision = 1,
          ProtectedValues = "unreadable-synthetic-bundle",
        }
      );
      using var company = f.Companies.As(other);
      await db.SaveChangesAsync();
    });
    var values = new IntegrationCredentialValues(
      new Dictionary<string, string>
      {
        ["phoneNumberId"] = "new-number",
        ["accessToken"] = "token",
        ["appSecret"] = "secret",
        ["verifyToken"] = "verify",
      }
    );
    await Assert.ThrowsAsync<InvalidOperationException>(
      () => f.Store.TryWriteAsync("whatsapp", 0, values, default)
    );
    await f.WithDbAsync(async db =>
      Assert.Empty(await db.IntegrationCredentialSettings.ToListAsync())
    );
  }

  private sealed class NoDeployment : IIntegrationDeploymentCredentials
  {
    public IntegrationCredentialValues Get(string provider) => new([]);
  }

  private sealed class ReadBarrier(IIntegrationCredentialStore inner)
    : IIntegrationCredentialStore
  {
    private int reads;
    public TaskCompletionSource FirstChecked { get; } = Signal();
    public TaskCompletionSource SecondChecked { get; } = Signal();
    public TaskCompletionSource ReleaseFirst { get; } = Signal();
    public TaskCompletionSource ReleaseSecond { get; } = Signal();

    private static TaskCompletionSource Signal() =>
      new(TaskCreationOptions.RunContinuationsAsynchronously);

    public Task<StoredIntegrationCredentials> ReadAsync(
      string provider,
      CancellationToken ct
    ) => inner.ReadAsync(provider, ct);

    public Task<bool> TryWriteAsync(
      string provider,
      long revision,
      IntegrationCredentialValues? values,
      CancellationToken ct
    ) => inner.TryWriteAsync(provider, revision, values, ct);

    public async Task<bool> HeldElsewhereAsync(
      string provider,
      string field,
      string value,
      CancellationToken ct
    )
    {
      var answer = await inner.HeldElsewhereAsync(provider, field, value, ct);
      switch (Interlocked.Increment(ref reads))
      {
        case 1:
          FirstChecked.SetResult();
          await ReleaseFirst.Task.WaitAsync(ct);
          break;
        case 2:
          SecondChecked.SetResult();
          await ReleaseSecond.Task.WaitAsync(ct);
          break;
      }
      return answer;
    }
  }
}
