using System.Data.Common;
using Application.Features.Integrations.Models;
using Application.Interfaces;
using Domain.Entities;
using Infrastructure.Persistence;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;

namespace Server.Tests.Persistence;

[Trait("Category", "Identity")]
[Trait("Kind", "Integration")]
public sealed class IntegrationAdmissionPostgresTests
{
  [RequiresPostgresFact]
  public async Task SeparateStoresSerializeChannelAdmissionBeforeReadingClaims()
  {
    await using var fixture = await PostgresFixture.CreateAsync();
    var other = Guid.NewGuid();
    await using (var db = fixture.Connect())
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
    }
    var companies = new TestCompany();
    var pause = new BeforeCommit();
    var entering = new BeforeLock();
    await using var firstServices = Services(pause);
    await using var secondServices = Services(entering);
    var protection = new EphemeralDataProtectionProvider();
    var first = new IntegrationCredentialStore(
      firstServices.GetRequiredService<IServiceScopeFactory>(),
      protection,
      TimeProvider.System
    );
    var second = new IntegrationCredentialStore(
      secondServices.GetRequiredService<IServiceScopeFactory>(),
      protection,
      TimeProvider.System
    );
    var values = new IntegrationCredentialValues(
      new Dictionary<string, string>
      {
        ["phoneNumberId"] = "same-channel",
        ["accessToken"] = "token",
        ["appSecret"] = "secret",
        ["verifyToken"] = "verify",
      }
    );
    var a = first.TryWriteAsync("whatsapp", 0, values, default);
    await pause.Entered.Task.WaitAsync(TimeSpan.FromSeconds(15));
    Task<bool> b;
    using (companies.As(other))
      b = second.TryWriteAsync("whatsapp", 0, values, default);
    try
    {
      await entering.Entered.Task.WaitAsync(TimeSpan.FromSeconds(15));
      Assert.False(b.IsCompleted);
    }
    finally
    {
      pause.Release.TrySetResult();
    }
    Assert.True(await a);
    await Assert.ThrowsAsync<IntegrationChannelConflictException>(() => b);
    await using var check = fixture.Connect();
    Assert.Single(
      await check
        .IntegrationCredentialSettings.IgnoreQueryFilters()
        .ToListAsync()
    );

    ServiceProvider Services(IInterceptor probe) =>
      new ServiceCollection()
        .AddSingleton<ICurrentCompany>(companies)
        .AddScoped(sp => fixture.Connect(sp, probe))
        .BuildServiceProvider(
          new ServiceProviderOptions { ValidateScopes = true }
        );
  }

  private sealed class BeforeCommit : SaveChangesInterceptor
  {
    public TaskCompletionSource Entered { get; } = Signal();
    public TaskCompletionSource Release { get; } = Signal();

    public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(
      DbContextEventData eventData,
      InterceptionResult<int> result,
      CancellationToken cancellationToken = default
    )
    {
      Entered.TrySetResult();
      await Release.Task.WaitAsync(TimeSpan.FromSeconds(30), cancellationToken);
      return result;
    }
  }

  private sealed class BeforeLock : DbCommandInterceptor
  {
    public TaskCompletionSource Entered { get; } = Signal();

    public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(
      DbCommand command,
      CommandEventData eventData,
      InterceptionResult<int> result,
      CancellationToken cancellationToken = default
    )
    {
      if (
        command.CommandText.Contains(
          "pg_advisory_xact_lock",
          StringComparison.Ordinal
        )
      )
        Entered.TrySetResult();
      return ValueTask.FromResult(result);
    }
  }

  private static TaskCompletionSource Signal() =>
    new(TaskCreationOptions.RunContinuationsAsynchronously);
}
