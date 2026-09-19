using System.Net;
using Infrastructure.Integrations.Samsara;

namespace Server.Tests.Fleet;

[Trait("Category", "Fleet")]
[Trait("Kind", "Integration")]
public sealed class SamsaraHosRecoveryTests
{
  [Fact]
  public async Task SettingsReadsReuseOneImmutableDriverProjectionAcrossTheCachedFleet()
  {
    using var fixture = new SamsaraProviderFixture();
    fixture.Drivers = Enumerable
      .Range(0, 2000)
      .Select(i => $"driver-{i}")
      .ToArray();
    var settings = await fixture.Catalog.GetSettingsAsync(
      "driver-1999",
      fixture.Api,
      default
    );
    var catalog = await fixture.Catalog.GetAsync(fixture.Api, default);
    catalog[^1].Timezone = "Changed by caller";
    for (var i = 0; i < 20; i++)
      Assert.Same(
        settings,
        await fixture.Catalog.GetSettingsAsync(
          "driver-1999",
          fixture.Api,
          default
        )
      );
    Assert.Equal("Etc/UTC", settings!.Timezone);
    Assert.Equal(1, fixture.CatalogCalls);
  }

  [Fact]
  public async Task FailureReturnsUnavailableButRecoveryKeepsTheIncrementalBaselineAcrossProviderScopes()
  {
    using var fixture = new SamsaraProviderFixture();
    var initial = (await fixture.Provider().GetAsync("driver", default))!;
    fixture.Clock.Advance(TimeSpan.FromMinutes(1));
    fixture.Read = (_, _) =>
      Task.FromResult(
        new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)
      );
    Assert.Null(await fixture.Provider().GetAsync("driver", default));
    fixture.Clock.Advance(TimeSpan.FromSeconds(30));
    Assert.Null(await fixture.Provider().GetAsync("driver", default));
    Assert.Equal(2, fixture.Requests.Count);
    fixture.Clock.Advance(TimeSpan.FromSeconds(31));
    fixture.Read = (request, _) =>
      Task.FromResult(SamsaraProviderFixture.Reply(request));
    var restored = (await fixture.Provider().GetAsync("driver", default))!;
    Assert.Equal(
      TimeSpan.FromDays(2),
      fixture.Requests[^1].To - fixture.Requests[^1].From
    );
    Assert.Equal(initial.From, restored.From);
    Assert.Contains(restored.Periods, period => period.Start == initial.From);
    Assert.Equal(fixture.Clock.GetUtcNow(), restored.Through);
    Assert.Equal(1, fixture.CatalogCalls);
  }

  [Fact]
  public async Task RecoveryDoesNotPostponeTheFifteenMinuteFullReconciliation()
  {
    using var fixture = new SamsaraProviderFixture();
    await fixture.Provider().GetAsync("driver", default);
    fixture.Clock.Advance(TimeSpan.FromMinutes(1));
    fixture.Read = (_, _) =>
      Task.FromResult(
        new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)
      );
    Assert.Null(await fixture.Provider().GetAsync("driver", default));
    fixture.Clock.Advance(TimeSpan.FromMinutes(14));
    fixture.Read = (request, _) =>
      Task.FromResult(SamsaraProviderFixture.Reply(request));
    Assert.NotNull(await fixture.Provider().GetAsync("driver", default));
    Assert.Equal(
      TimeSpan.FromDays(16),
      fixture.Requests[^1].To - fixture.Requests[^1].From
    );
    Assert.Equal(2, fixture.CatalogCalls);
  }

  [Fact]
  public async Task CallerCancellationDoesNotEraseTheBaselineOrCreateAFailureCooldown()
  {
    using var fixture = new SamsaraProviderFixture();
    await fixture.Provider().GetAsync("driver", default);
    fixture.Clock.Advance(TimeSpan.FromMinutes(1));
    using var cancellation = new CancellationTokenSource();
    fixture.Read = (_, ct) =>
    {
      cancellation.Cancel();
      ct.ThrowIfCancellationRequested();
      throw new InvalidOperationException();
    };
    await Assert.ThrowsAnyAsync<OperationCanceledException>(
      () => fixture.Provider().GetAsync("driver", cancellation.Token)
    );
    fixture.Read = (request, _) =>
      Task.FromResult(SamsaraProviderFixture.Reply(request));
    Assert.NotNull(await fixture.Provider().GetAsync("driver", default));
    Assert.Equal(3, fixture.Requests.Count);
    Assert.Equal(
      TimeSpan.FromDays(2),
      fixture.Requests[^1].To - fixture.Requests[^1].From
    );
  }

  [Fact]
  public async Task SameDriverSharesOneRequestButAnUnrelatedDriverDoesNotWaitForItsHistory()
  {
    using var fixture = new SamsaraProviderFixture();
    var other = Enumerable
      .Range(0, 1000)
      .Select(i => $"other-{i}")
      .First(id =>
        !ReferenceEquals(
          fixture.History.Gate("driver"),
          fixture.History.Gate(id)
        )
      );
    fixture.Drivers = ["driver", other];
    await fixture.Catalog.GetAsync(fixture.Api, default);
    var entered = new TaskCompletionSource(
      TaskCreationOptions.RunContinuationsAsynchronously
    );
    var release = new TaskCompletionSource(
      TaskCreationOptions.RunContinuationsAsynchronously
    );
    fixture.Read = async (request, ct) =>
    {
      if (request.Driver == "driver")
      {
        entered.TrySetResult();
        await release.Task.WaitAsync(ct);
      }
      return SamsaraProviderFixture.Reply(request);
    };
    var first = fixture.Provider().GetAsync("driver", default);
    await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
    var shared = fixture.Provider().GetAsync("driver", default);
    try
    {
      Assert.NotNull(
        await fixture
          .Provider()
          .GetAsync(other, default)
          .WaitAsync(TimeSpan.FromSeconds(5))
      );
    }
    finally
    {
      release.TrySetResult();
    }
    Assert.Same(await first, await shared);
    Assert.Equal(2, fixture.Requests.Count);
    Assert.Equal(1, fixture.CatalogCalls);
  }

  [Fact]
  public async Task CatalogFailureHasOneSharedCooldownWithoutServingOldSettings()
  {
    using var fixture = new SamsaraProviderFixture();
    fixture.Drivers = ["driver", "other"];
    await fixture.Provider().GetAsync("driver", default);
    fixture.Clock.Advance(TimeSpan.FromMinutes(15));
    fixture.CatalogStatus = HttpStatusCode.ServiceUnavailable;
    Assert.Null(await fixture.Provider().GetAsync("driver", default));
    Assert.Null(await fixture.Provider().GetAsync("other", default));
    Assert.Equal(2, fixture.CatalogCalls);
    Assert.Single(fixture.Requests);
    fixture.Clock.Advance(TimeSpan.FromMinutes(1));
    fixture.CatalogStatus = HttpStatusCode.OK;
    Assert.NotNull(await fixture.Provider().GetAsync("other", default));
    Assert.Equal(3, fixture.CatalogCalls);
  }

  [Fact]
  public async Task ExplicitCatalogRefreshUpdatesTheSharedSnapshotWithoutLeakingMutableMetadata()
  {
    using var fixture = new SamsaraProviderFixture();
    var original = await fixture.Catalog.GetAsync(fixture.Api, default);
    original[0].Timezone = "Changed";
    Assert.Equal(
      "Etc/UTC",
      (await fixture.Catalog.GetAsync(fixture.Api, default))[0].Timezone
    );
    fixture.Drivers = ["replacement"];
    await new SamsaraFleetProvider(
      fixture.Api,
      fixture.Catalog
    ).GetDriversAsync();
    Assert.Equal(
      "replacement",
      Assert.Single(await fixture.Catalog.GetAsync(fixture.Api, default)).Id
    );
    Assert.Equal(2, fixture.CatalogCalls);
  }

  [Fact]
  public async Task FailedExplicitRefreshDoesNotInvalidateAStillFreshCatalogOrExtendItsLifetime()
  {
    using var fixture = new SamsaraProviderFixture();
    await fixture.Catalog.GetAsync(fixture.Api, default);
    fixture.Clock.Advance(TimeSpan.FromMinutes(1));
    fixture.CatalogStatus = HttpStatusCode.ServiceUnavailable;
    await Assert.ThrowsAsync<HttpRequestException>(
      () => fixture.Catalog.GetAsync(fixture.Api, default, forceRefresh: true)
    );
    Assert.Single(await fixture.Catalog.GetAsync(fixture.Api, default));
    Assert.Equal(2, fixture.CatalogCalls);
    fixture.Clock.Advance(TimeSpan.FromMinutes(14));
    await Assert.ThrowsAsync<HttpRequestException>(
      () => fixture.Catalog.GetAsync(fixture.Api, default)
    );
    Assert.Equal(3, fixture.CatalogCalls);
  }
}
