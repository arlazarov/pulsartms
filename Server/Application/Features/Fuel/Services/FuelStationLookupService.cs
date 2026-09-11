using System.Security.Cryptography;
using System.Text;
using Application.Features.Fuel.Interfaces;
using Application.Features.Fuel.Models;
using Application.Features.Fuel.Exceptions;

namespace Application.Features.Fuel.Services;

public sealed class FuelStationLookupService(IFuelStationLookupStore store, IPlaceSearchService places, TimeProvider clock)
{
  public async Task<PlaceSearchResult?> FindAsync(string stationId, string query, CancellationToken ct) =>
    (await PrepareAsync(stationId, query, ct)).Place;

  public Task<bool> IsCurrentAsync(string stationId, string revision, CancellationToken ct) => store.IsCurrentAsync(stationId, revision, ct);

  public async Task<FuelStationLookupResult> PrepareAsync(string stationId, string query, CancellationToken ct)
  {
    var now = clock.GetUtcNow().UtcDateTime;
    var signature = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(query.Trim().ToUpperInvariant())));
    var state = await store.ReadAsync(stationId, ct);
    if (state?.Signature == signature && !string.IsNullOrEmpty(state.Revision) && state.NextAttemptAt > now) return Cached(state);
    var owner = Guid.NewGuid().ToString("N");
    if (!await store.AcquireAsync(stationId, owner, now, ct)) throw new FuelStationLookupDeferredException();
    var failed = false;
    try
    {
      state = await store.ReadAsync(stationId, ct);
      if (state?.Signature == signature && !string.IsNullOrEmpty(state.Revision) && state.NextAttemptAt > now) return Cached(state);
      if (state?.Signature != signature) state = new() { Signature = signature };
      state!.Failures = Math.Clamp(state.Failures, 0, 9) + 1;
      state.NextAttemptAt = now.AddMinutes(Math.Min(360, 15 * Math.Pow(2, state.Failures - 1)));
      state.Result = null;
      state.Revision = Guid.NewGuid().ToString("N");
      state.Pending = true;
      state.ErrorCode = null;
      // This independent reservation must survive a failed or cancelled email transaction.
      await store.SaveAsync(stationId, owner, state, ct);
      using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
      timeout.CancelAfter(TimeSpan.FromSeconds(30));
      try
      {
        var result = await places.SearchAsync(query, timeout.Token);
        state.Result = result is not null && HasLocation(result.Latitude, result.Longitude) ? result : null;
        state.NextAttemptAt = now.AddHours(state.Result is null ? 1 : 24);
        state.Failures = 0;
        state.Pending = false;
        state.ErrorCode = null;
        await store.SaveAsync(stationId, owner, state, ct);
        return new(state.Revision, state.Result);
      }
      catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
      catch (Exception ex)
      {
        state.Pending = false;
        state.ErrorCode = ex.GetType().Name;
        try { await store.SaveAsync(stationId, owner, state, ct); }
        catch { /* The reservation remains durable; preserve the original provider failure. */ }
        throw;
      }
    }
    catch { failed = true; throw; }
    finally
    {
      using var release = new CancellationTokenSource(TimeSpan.FromSeconds(5));
      try { await store.ReleaseAsync(stationId, owner, release.Token); }
      catch when (failed) { /* Lease expiry bounds cleanup after a failed attempt. */ }
    }
  }

  private static FuelStationLookupResult Cached(FuelStationLookupState state) => state.Pending || state.ErrorCode is not null
    ? throw new FuelStationLookupDeferredException() : new(state.Revision, state.Result);

  public static bool HasLocation(decimal? latitude, decimal? longitude) =>
    latitude is >= -90 and <= 90 && longitude is >= -180 and <= 180 && (latitude != 0 || longitude != 0);
}
