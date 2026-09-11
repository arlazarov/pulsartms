using Application.Features.Fuel.Commands.ImportFuelDiscounts;
using Application.Features.Fuel.Interfaces;
using Application.Features.Fuel.Models;

namespace Application.Features.Fuel.Services;

public sealed class GmailWatchLifecycle(IGmailWatchStore store, IGmailWatchService watch, ISender sender, TimeProvider clock)
{
  public async Task<GmailWatchRunResult> RunAsync(bool register, CancellationToken ct)
  {
    var now = clock.GetUtcNow().UtcDateTime;
    var state = await store.ReadAsync(ct);
    if (!register && (state is null || state.NextRenewalAt > now && state.NextRecoveryAt > now)) return new();
    var owner = Guid.NewGuid().ToString("N");
    if (!await store.AcquireAsync(owner, now, ct)) return new(Busy: true);
    try
    {
      state = await store.ReadAsync(ct);
      if (state is null && !register) return new();
      state ??= new() { RegisteredAt = now, NextRenewalAt = now, NextRecoveryAt = now };
      using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
      timeout.CancelAfter(TimeSpan.FromSeconds(90));
      GmailWatchResult? result = null;
      string? renewalError = null, recoveryError = null;
      if (register || state.NextRenewalAt <= now)
      {
        state.RenewalFailures = Math.Clamp(state.RenewalFailures, 0, 9) + 1;
        state.NextRenewalAt = now.Add(RetryDelay(state.RenewalFailures));
        // Reserve the retry before the provider request so process restarts cannot retry immediately.
        await store.SaveAsync(owner, state, ct);
        try
        {
          result = await watch.StartAsync(timeout.Token);
          var expiration = result.Expiration is { } milliseconds
            ? DateTimeOffset.FromUnixTimeMilliseconds(milliseconds).UtcDateTime : DateTime.MinValue;
          if (expiration <= now.AddMinutes(15)) throw new InvalidOperationException("Gmail watch expiration is missing or too soon.");
          state.ExpiresAt = expiration;
          state.HistoryId = result.HistoryId;
          state.LastRenewedAt = now;
          state.RenewalFailures = 0;
          state.RenewalErrorCode = null;
          var next = expiration.AddHours(-6) < now.AddDays(1) ? expiration.AddHours(-6) : now.AddDays(1);
          state.NextRenewalAt = next > now.AddMinutes(15) ? next : now.AddMinutes(15);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception ex)
        {
          renewalError = state.RenewalErrorCode = ex.GetType().Name;
          result = null;
        }
        await store.SaveAsync(owner, state, ct);
      }
      if (!register && state.NextRecoveryAt <= now && !timeout.IsCancellationRequested)
      {
        state.RecoveryFailures = Math.Clamp(state.RecoveryFailures, 0, 9) + 1;
        state.NextRecoveryAt = now.Add(RetryDelay(state.RecoveryFailures));
        await store.SaveAsync(owner, state, ct);
        try
        {
          var imported = await sender.Send(new ImportFuelDiscountsCommand(), timeout.Token);
          if (!imported.Success) throw new InvalidOperationException("Fuel notification recovery failed.");
          state.LastRecoveredAt = now;
          state.NextRecoveryAt = now.AddHours(2);
          state.RecoveryFailures = 0;
          state.RecoveryErrorCode = null;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception ex) { recoveryError = state.RecoveryErrorCode = ex.GetType().Name; }
        await store.SaveAsync(owner, state, ct);
      }
      return new(result, RenewalError: renewalError, RecoveryError: recoveryError);
    }
    finally
    {
      using var release = new CancellationTokenSource(TimeSpan.FromSeconds(5));
      await store.ReleaseAsync(owner, release.Token);
    }
  }

  private static TimeSpan RetryDelay(int failures) => TimeSpan.FromMinutes(Math.Min(360, 15 * Math.Pow(2, failures - 1)));
}
