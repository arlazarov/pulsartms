using System.Text.Json;
using Application.Features.Fuel.Commands.ImportFuelDiscounts;
using Application.Features.Fuel.Interfaces;
using Application.Features.Fuel.Models;
using Application.Features.Fuel.Services;
using Application.Models;
using MediatR;

namespace Server.Tests.Fuel;

[Trait("Category", "Fuel")]
[Trait("Kind", "Unit")]
public sealed class GmailWatchLifecycleTests
{
  [Fact]
  public async Task UnregisteredMailboxNeverStartsBackgroundAuthorizationOrImport()
  {
    var fixture = new Fixture();
    await fixture.Run();
    Assert.Equal(0, fixture.Watch.Calls);
    Assert.Equal(0, fixture.Sender.Calls);
    Assert.Equal(0, fixture.Store.Acquisitions);
  }

  [Fact]
  public async Task RegistrationPersistsUtcExpirationAndDailyRenewalAcrossInstances()
  {
    var fixture = new Fixture();
    var now = fixture.Clock.Now.UtcDateTime;
    Assert.NotNull((await fixture.Run(true)).Watch);
    var registered = fixture.Store.State!;
    Assert.Equal(now, registered.RegisteredAt);
    Assert.Equal(now.AddDays(7), registered.ExpiresAt);
    Assert.Equal(DateTimeKind.Utc, registered.ExpiresAt!.Value.Kind);
    Assert.Equal(now.AddDays(1), registered.NextRenewalAt);
    Assert.Equal(42UL, registered.HistoryId);
    Assert.Equal(0, fixture.Sender.Calls);
    await fixture.Run();
    await fixture.Run();
    Assert.Equal(1, fixture.Watch.Calls);
    Assert.Equal(1, fixture.Sender.Calls);
    Assert.Equal(now.AddHours(2), fixture.Store.State!.NextRecoveryAt);
    fixture.Clock.Now = new(now.AddDays(1), TimeSpan.Zero);
    await fixture.Run();
    Assert.Equal(2, fixture.Watch.Calls);
    Assert.Equal(2, fixture.Sender.Calls);
  }

  [Fact]
  public async Task FailedRenewalsKeepPreviousExpirationAndPersistCappedRetryBudget()
  {
    var fixture = new Fixture();
    await fixture.Run(true);
    var expiration = fixture.Store.State!.ExpiresAt;
    fixture.Watch.Error = new InvalidOperationException("Credentials are unavailable");
    fixture.Clock.Now = new(fixture.Store.State.NextRenewalAt, TimeSpan.Zero);
    foreach (var delay in new[] { 15, 30, 60, 120, 240, 360, 360 })
    {
      var now = fixture.Clock.Now;
      var result = await fixture.Run();
      Assert.Equal(nameof(InvalidOperationException), result.RenewalError);
      Assert.Null(result.Watch);
      var state = fixture.Store.State!;
      Assert.Equal(now.UtcDateTime.AddMinutes(delay), state.NextRenewalAt);
      Assert.Equal(expiration, state.ExpiresAt);
      var calls = fixture.Watch.Calls;
      await fixture.Run();
      Assert.Equal(calls, fixture.Watch.Calls);
      fixture.Clock.Now = new(state.NextRenewalAt, TimeSpan.Zero);
    }
  }

  [Fact]
  public async Task MissingExpirationDoesNotClaimHealthyWatch()
  {
    var fixture = new Fixture();
    fixture.Watch.Result = new() { HistoryId = 42 };
    Assert.Equal(nameof(InvalidOperationException), (await fixture.Run(true)).RenewalError);
    Assert.Null(fixture.Store.State!.LastRenewedAt);
    Assert.Null(fixture.Store.State.ExpiresAt);
    Assert.Equal(fixture.Clock.Now.UtcDateTime.AddMinutes(15), fixture.Store.State.NextRenewalAt);
  }

  [Fact]
  public async Task CancelledAttemptReservesRetryBeforeCallingProvider()
  {
    var fixture = new Fixture();
    using var cancellation = new CancellationTokenSource();
    fixture.Watch.BeforeCall = () =>
    {
      Assert.Equal(fixture.Clock.Now.UtcDateTime.AddMinutes(15), fixture.Store.State!.NextRenewalAt);
      cancellation.Cancel();
      throw new OperationCanceledException(cancellation.Token);
    };
    await Assert.ThrowsAnyAsync<OperationCanceledException>(() => fixture.Run(true, cancellation.Token));
    fixture.Watch.BeforeCall = null;
    await fixture.Run();
    Assert.Equal(1, fixture.Watch.Calls);
    Assert.Equal(2, fixture.Store.Releases);
  }

  [Fact]
  public async Task FailedRecoveryReservesRetryWithoutChangingRenewalSchedule()
  {
    var fixture = new Fixture();
    await fixture.Run(true);
    fixture.Sender.Success = false;
    Assert.Equal(nameof(InvalidOperationException), (await fixture.Run()).RecoveryError);
    var state = fixture.Store.State!;
    Assert.Equal(fixture.Clock.Now.UtcDateTime.AddMinutes(15), state.NextRecoveryAt);
    Assert.Equal(fixture.Clock.Now.UtcDateTime.AddDays(1), state.NextRenewalAt);
    await fixture.Run();
    Assert.Equal(1, fixture.Sender.Calls);
    fixture.Clock.Now = new(state.NextRecoveryAt, TimeSpan.Zero);
    fixture.Sender.Success = true;
    await fixture.Run();
    Assert.Equal(2, fixture.Sender.Calls);
    Assert.Equal(0, fixture.Store.State!.RecoveryFailures);
    Assert.Null(fixture.Store.State.RecoveryErrorCode);
  }

  [Fact]
  public async Task BusyOwnerAndStateRefreshedAfterLeaseAcquisitionPreventDuplicateCalls()
  {
    var fixture = new Fixture();
    await fixture.Run(true);
    fixture.Store.Busy = true;
    Assert.True((await fixture.Run()).Busy);
    Assert.Equal(0, fixture.Sender.Calls);
    fixture.Store.Busy = false;
    fixture.Store.BeforeAcquire = () =>
    {
      var state = fixture.Store.State!;
      state.NextRecoveryAt = fixture.Clock.Now.UtcDateTime.AddHours(2);
      fixture.Store.State = state;
    };
    await fixture.Run();
    Assert.Equal(1, fixture.Watch.Calls);
    Assert.Equal(0, fixture.Sender.Calls);
  }

  private sealed class Fixture
  {
    public Clock Clock { get; } = new();
    public Store Store { get; } = new();
    public Watch Watch { get; } = new();
    public Sender Sender { get; } = new();
    public Fixture() => Watch.Result = new() { HistoryId = 42, Expiration = Clock.Now.AddDays(7).ToUnixTimeMilliseconds() };
    public Task<GmailWatchRunResult> Run(bool register = false, CancellationToken ct = default) =>
      new GmailWatchLifecycle(Store, Watch, Sender, Clock).RunAsync(register, ct);
  }

  private sealed class Clock : TimeProvider
  {
    public DateTimeOffset Now = new(2026, 9, 7, 12, 0, 0, TimeSpan.Zero);
    public override DateTimeOffset GetUtcNow() => Now;
  }

  private sealed class Store : IGmailWatchStore
  {
    private string? json;
    public GmailWatchState? State
    {
      get => json is null ? null : JsonSerializer.Deserialize<GmailWatchState>(json);
      set => json = JsonSerializer.Serialize(value);
    }
    public bool Busy;
    public int Acquisitions, Releases;
    public Action? BeforeAcquire;
    public Task<bool> AcquireAsync(string owner, DateTime now, CancellationToken ct)
    {
      Acquisitions++;
      BeforeAcquire?.Invoke();
      return Task.FromResult(!Busy);
    }
    public Task<GmailWatchState?> ReadAsync(CancellationToken ct) => Task.FromResult(State);
    public Task SaveAsync(string owner, GmailWatchState state, CancellationToken ct) { State = state; return Task.CompletedTask; }
    public Task ReleaseAsync(string owner, CancellationToken ct) { Releases++; return Task.CompletedTask; }
  }

  private sealed class Watch : IGmailWatchService
  {
    public int Calls;
    public Action? BeforeCall;
    public Exception? Error;
    public GmailWatchResult Result = new();
    public Task<GmailWatchResult> StartAsync(CancellationToken cancellationToken = default)
    {
      Calls++;
      BeforeCall?.Invoke();
      return Error is null ? Task.FromResult(Result) : Task.FromException<GmailWatchResult>(Error);
    }
  }

  private sealed class Sender : ISender
  {
    public int Calls;
    public bool Success = true;
    public Task<TResponse> Send<TResponse>(IRequest<TResponse> request, CancellationToken ct = default)
    {
      Assert.IsType<ImportFuelDiscountsCommand>(request);
      Calls++;
      object response = Success ? RequestResponse<int>.Ok(0) : RequestResponse<int>.Fail("Import unavailable", 503);
      return Task.FromResult((TResponse)response);
    }
    public Task Send<TRequest>(TRequest request, CancellationToken ct = default) where TRequest : IRequest => throw new NotSupportedException();
    public Task<object?> Send(object request, CancellationToken ct = default) => throw new NotSupportedException();
    public IAsyncEnumerable<TResponse> CreateStream<TResponse>(IStreamRequest<TResponse> request, CancellationToken ct = default) => throw new NotSupportedException();
    public IAsyncEnumerable<object?> CreateStream(object request, CancellationToken ct = default) => throw new NotSupportedException();
  }
}
