using Application.Caching;
using Application.Features.Fuel.Models;
using Application.Features.Fuel.Services;
using Application.Features.Synchronization.Options;
using Microsoft.Extensions.Options;

namespace Server.Tests.Fuel;

[Trait("Category", "Fuel")]
[Trait("Kind", "Unit")]
public sealed class FuelExchangeRateServiceTests
{
  [Theory]
  [InlineData("changed")]
  [InlineData("expired")]
  [InlineData("removed")]
  public async Task UncachedRateReadsRetainValidationAndNeverFetchTheProvider(
    string change
  )
  {
    using var fixture = new Fixture();
    var previous = fixture.Store.Rate = fixture.Rate(.71m);
    Assert.Equal(previous, await fixture.Service.ReadAsync(default));
    fixture.Store.Rate = change switch
    {
      "changed" => fixture.Rate(.79m),
      "expired" => fixture.Rate() with { ObservedOn = new(2026, 9, 8) },
      _ => null,
    };

    var fresh = await fixture.Service.ReadUncachedAsync(default);

    Assert.Equal(change == "changed" ? fixture.Store.Rate : null, fresh);
    Assert.Equal(2, fixture.Store.Reads);
    Assert.Equal(previous, await fixture.Service.ReadAsync(default));
    Assert.Equal(0, fixture.Provider.Calls);
    Assert.Equal(0, fixture.Store.Saves);
  }

  [Theory]
  [InlineData("absent")]
  [InlineData("stale")]
  [InlineData("future")]
  [InlineData("range")]
  public async Task InvalidSavedRateIsUnavailableWithoutRefreshingOnRead(
    string state
  )
  {
    using var fixture = new Fixture();
    fixture.Store.Rate = state switch
    {
      "absent" => null,
      "stale" => fixture.Rate() with { ObservedOn = new(2026, 9, 8) },
      "future" => fixture.Rate() with
      {
        RetrievedAt = fixture.Clock.Now.UtcDateTime.AddSeconds(1),
      },
      "range" => fixture.Rate(0),
      _ => throw new InvalidOperationException(),
    };
    Assert.Null(await fixture.Service.ReadAsync(default));
    Assert.Equal(0, fixture.Provider.Calls);
    Assert.Equal(0, fixture.Store.Saves);
    Assert.Equal(0, fixture.Store.Releases);
  }

  [Fact]
  public async Task CachedReadNeverCallsProviderAndRechecksAgeAfterClockAdvances()
  {
    using var fixture = new Fixture();
    var expected = fixture.Store.Rate = fixture.Rate();
    Assert.Equal(expected, await fixture.Service.ReadAsync(default));
    Assert.Equal(expected, await fixture.Service.ReadAsync(default));
    Assert.Equal(1, fixture.Store.Reads);
    fixture.Clock.Now = fixture.Clock.Now.AddDays(8);
    Assert.Null(await fixture.Service.ReadAsync(default));
    Assert.Equal(1, fixture.Store.Reads);
    Assert.Equal(0, fixture.Provider.Calls);
    Assert.Equal(0, fixture.Store.Saves);
  }

  [Fact]
  public async Task RefreshWaitsOneHourAndInvalidatesCachedPreviousValue()
  {
    using var fixture = new Fixture();
    var original = fixture.Store.Rate = fixture.Rate(.71m);
    Assert.Equal(original, await fixture.Service.ReadAsync(default));
    fixture.Clock.Now = fixture.Clock.Now.AddMinutes(59);
    Assert.False(await fixture.Service.RefreshAsync(default));
    Assert.Equal(0, fixture.Provider.Calls);
    fixture.Clock.Now = fixture.Clock.Now.AddMinutes(1);
    Assert.True(await fixture.Service.RefreshAsync(default));
    var updated = await fixture.Service.ReadAsync(default);
    Assert.Equal(.75m, updated!.UsdPerCad);
    Assert.Equal(fixture.Clock.Now.UtcDateTime, updated.RetrievedAt);
    Assert.Equal(1, fixture.Provider.Calls);
    Assert.Equal(1, fixture.Store.Saves);
    Assert.Equal(1, fixture.Store.Releases);
  }

  [Fact]
  public async Task LeaseRecheckUsesAnAlreadyPublishedFreshRate()
  {
    using var fixture = new Fixture();
    fixture.Store.OnAcquire = () => fixture.Store.Rate = fixture.Rate();
    Assert.False(await fixture.Service.RefreshAsync(default));
    Assert.Equal(0, fixture.Provider.Calls);
    Assert.Equal(0, fixture.Store.Saves);
    Assert.Equal(1, fixture.Store.Releases);
  }

  [Fact]
  public async Task ConcurrentRefreshesShareTheStoreLease()
  {
    using var fixture = new Fixture();
    var entered = new TaskCompletionSource(
      TaskCreationOptions.RunContinuationsAsynchronously
    );
    var release = new TaskCompletionSource(
      TaskCreationOptions.RunContinuationsAsynchronously
    );
    fixture.Provider.Read = async ct =>
    {
      entered.SetResult();
      await release.Task.WaitAsync(ct);
      return fixture.Rate();
    };
    var first = fixture.Service.RefreshAsync(default);
    await entered.Task;
    Assert.False(await fixture.Service.RefreshAsync(default));
    release.SetResult();
    Assert.True(await first);
    Assert.Equal(1, fixture.Provider.Calls);
    Assert.Equal(1, fixture.Store.Saves);
    Assert.Equal(1, fixture.Store.Releases);
  }

  [Theory]
  [InlineData("provider")]
  [InlineData("cancellation")]
  [InlineData("low")]
  [InlineData("high")]
  [InlineData("stale")]
  [InlineData("future-observation")]
  [InlineData("future-retrieval")]
  [InlineData("local-retrieval")]
  [InlineData("default-retrieval")]
  [InlineData("observation-after-retrieval")]
  public async Task FailedRefreshKeepsLastGoodRateAndReleasesLease(
    string failure
  )
  {
    using var fixture = new Fixture();
    var original = fixture.Store.Rate = fixture.Rate(.71m) with
    {
      RetrievedAt = fixture.Clock.Now.UtcDateTime.AddHours(-2),
    };
    var candidate = fixture.Rate();
    fixture.Provider.Read = _ =>
      failure switch
      {
        "provider" => throw new HttpRequestException("Unavailable"),
        "cancellation" => throw new OperationCanceledException(),
        "low" => Task.FromResult(candidate with { UsdPerCad = .09m }),
        "high" => Task.FromResult(candidate with { UsdPerCad = 2.01m }),
        "stale" => Task.FromResult(
          candidate with
          {
            ObservedOn = candidate.ObservedOn.AddDays(-8),
          }
        ),
        "future-observation" => Task.FromResult(
          candidate with
          {
            ObservedOn = candidate.ObservedOn.AddDays(1),
          }
        ),
        "future-retrieval" => Task.FromResult(
          candidate with
          {
            RetrievedAt = candidate.RetrievedAt.AddSeconds(1),
          }
        ),
        "local-retrieval" => Task.FromResult(
          candidate with
          {
            RetrievedAt = DateTime.SpecifyKind(
              candidate.RetrievedAt,
              DateTimeKind.Unspecified
            ),
          }
        ),
        "default-retrieval" => Task.FromResult(
          candidate with
          {
            RetrievedAt = default,
          }
        ),
        "observation-after-retrieval" => Task.FromResult(
          candidate with
          {
            RetrievedAt = candidate.RetrievedAt.AddDays(-1),
          }
        ),
        _ => throw new InvalidOperationException(),
      };
    await Assert.ThrowsAnyAsync<Exception>(
      () => fixture.Service.RefreshAsync(default)
    );
    Assert.Equal(original, fixture.Store.Rate);
    Assert.Equal(original, await fixture.Service.ReadAsync(default));
    Assert.Equal(0, fixture.Store.Saves);
    Assert.Equal(1, fixture.Store.Releases);
    fixture.Provider.Read = _ => Task.FromResult(fixture.Rate());
    Assert.True(await fixture.Service.RefreshAsync(default));
  }

  [Theory]
  [InlineData(.1)]
  [InlineData(2)]
  public async Task SevenDayObservationAndInclusiveRateBoundsRemainUsable(
    double value
  )
  {
    using var fixture = new Fixture();
    fixture.Store.Rate = fixture.Rate((decimal)value) with
    {
      ObservedOn = DateOnly
        .FromDateTime(fixture.Clock.Now.UtcDateTime)
        .AddDays(-7),
    };
    Assert.Equal(
      (decimal)value,
      (await fixture.Service.ReadAsync(default))!.UsdPerCad
    );
    Assert.False(await fixture.Service.RefreshAsync(default));
    Assert.Equal(0, fixture.Provider.Calls);
  }

  [Fact]
  public async Task OlderObservationCannotOverwriteTheNewerSavedSourceDate()
  {
    using var fixture = new Fixture();
    var original = fixture.Store.Rate = fixture.Rate(.71m) with
    {
      RetrievedAt = fixture.Clock.Now.UtcDateTime.AddHours(-2),
    };
    fixture.Provider.Read = _ =>
      Task.FromResult(
        fixture.Rate() with
        {
          ObservedOn = original.ObservedOn.AddDays(-1),
        }
      );
    Assert.False(await fixture.Service.RefreshAsync(default));
    Assert.Equal(original, fixture.Store.Rate);
    Assert.Equal(0, fixture.Store.Saves);
    Assert.Equal(1, fixture.Store.Releases);
  }

  private sealed class Fixture : IDisposable
  {
    public Clock Clock { get; } = new();
    public MemoryFuelExchangeRateStore Store { get; } = new();
    public StubFuelExchangeRateProvider Provider { get; } = new();
    public ReadCache Reads { get; } =
      new(Options.Create(new SynchronizationOptions()));
    public FuelExchangeRateService Service { get; }

    public Fixture()
    {
      Provider.Read = _ => Task.FromResult(Rate());
      Service = new(Store, Provider, Reads, Clock);
    }

    public FuelExchangeRate Rate(decimal value = .75m) =>
      new(
        value,
        DateOnly.FromDateTime(Clock.Now.UtcDateTime),
        Clock.Now.UtcDateTime
      );

    public void Dispose() => Reads.Dispose();
  }

  private sealed class Clock : TimeProvider
  {
    public DateTimeOffset Now { get; set; } =
      new(2026, 9, 16, 18, 0, 0, TimeSpan.Zero);

    public override DateTimeOffset GetUtcNow() => Now;
  }
}
