using System.Buffers;
using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Application.Diagnostics;
using Application.Features.Routing.Interfaces;
using Application.Interfaces;
using Domain.Entities.Dispatch;
using Domain.Rules;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace Infrastructure.Integrations.TomTom;

// Spending a request. TomTom is paid for per call and limited per day and
// per minute, so a request is first looked for in the cache, then reserved
// in the database under a lock - the reservation survives a cancelled call
// or a lost process - and only then sent.
public sealed partial class TomTomRoutingProvider
{
  private async Task<T> CachedAsync<T>(
    string operation,
    string query,
    TimeSpan lifetime,
    Func<JsonElement, T> parse,
    Func<string, bool, T> readCached,
    Func<T, string> serialize,
    CancellationToken ct
  )
  {
    if (!IsConfigured)
      throw new RoutePlanningException(
        "TomTom API key is not configured on the server."
      );
    var legacyHash = Convert.ToHexString(
      SHA256.HashData(Encoding.UTF8.GetBytes(query))
    );
    // Rolling or rolled-back binaries must not read the legs-only cache as a
    // full route.
    var hash = Convert.ToHexString(
      SHA256.HashData(Encoding.UTF8.GetBytes("route-legs-v1:" + query))
    );
    var cachedAt = DateTime.UtcNow;
    // A lookup happens before the reservation is attempted at all, outside its
    // lock, and a second one follows only when this one found a stale entry.
    // They are round trips like any other, so they are timed as themselves
    // rather than assumed - `cache-recheck` runs on its own branch and its
    // count is not the count of paid calls.
    var probing = Stopwatch.GetTimestamp();
    var existing = await db
      .RoutingApiCalls.AsNoTracking()
      .Where(x =>
        (x.RequestHash == hash || x.RequestHash == legacyHash)
        && x.ExpiresAt > cachedAt
      )
      .OrderByDescending(x => x.RequestHash == hash)
      .ThenByDescending(x => x.CreatedAt)
      .FirstOrDefaultAsync(ct);
    PerformanceStages.Elapsed("routing", "cache-probe", probing);
    if (existing?.ResultJson is { } existingJson)
      return readCached(existingJson, existing.RequestHash == hash);
    using (await RequestGates.EnterAsync(hash, ct))
    {
      if (existing is not null)
      {
        var refreshedAt = DateTime.UtcNow;
        var rechecking = Stopwatch.GetTimestamp();
        var refreshed = await db
          .RoutingApiCalls.AsNoTracking()
          .Where(x =>
            (x.RequestHash == hash || x.RequestHash == legacyHash)
            && x.ExpiresAt > refreshedAt
          )
          .OrderByDescending(x => x.RequestHash == hash)
          .ThenByDescending(x => x.CreatedAt)
          .FirstOrDefaultAsync(ct);
        PerformanceStages.Elapsed("routing", "cache-recheck", rechecking);
        if (refreshed?.ResultJson is { } refreshedJson)
          return readCached(refreshedJson, refreshed.RequestHash == hash);
        if (refreshed is not null)
          throw new RoutePlanningException(
            refreshed.ErrorMessage ?? "This route request is waiting to retry.",
            refreshed.ExpiresAt
          );
      }
      PerformanceStages.Count("routing", "provider-attempts-queued", 1);
      using (PerformanceStages.Start("routing", "provider-slot-wait"))
        await ProviderSlots.WaitAsync(ct);
      try
      {
        return await CalculateReservedAsync(
          operation,
          query,
          lifetime,
          parse,
          readCached,
          serialize,
          hash,
          legacyHash,
          ct
        );
      }
      finally
      {
        ProviderSlots.Release();
      }
    }
  }

  private async Task<T> CalculateReservedAsync<T>(
    string operation,
    string query,
    TimeSpan lifetime,
    Func<JsonElement, T> parse,
    Func<string, bool, T> readCached,
    Func<T, string> serialize,
    string hash,
    string legacyHash,
    CancellationToken ct
  )
  {
    RoutingApiCall? call = null;
    try
    {
      DateTime now;
      using (PerformanceStages.Start("routing", "reservation-wait"))
        await ReservationGate.WaitAsync(ct);
      try
      {
        // "reservation-total" CONTAINS every stage started below it. Read it
        // as the cost of reserving, and read the others as its breakdown -
        // adding them to it counts the same milliseconds twice.
        using var reserving = PerformanceStages.Start(
          "routing",
          "reservation-total"
        );
        var opening = Stopwatch.GetTimestamp();
        await using var transaction = await db.Database.BeginTransactionAsync(
          ct
        );
        PerformanceStages.Elapsed("routing", "reservation-begin", opening);
        if (db.Database.IsNpgsql())
        {
          var locking = Stopwatch.GetTimestamp();
          await db.Database.ExecuteSqlRawAsync(
            "SELECT pg_advisory_xact_lock(710246710)",
            ct
          );
          PerformanceStages.Elapsed("routing", "reservation-lock", locking);
        }
        now = DateTime.UtcNow;
        var looking = Stopwatch.GetTimestamp();
        var cached = await db
          .RoutingApiCalls.AsNoTracking()
          .Where(x =>
            (x.RequestHash == hash || x.RequestHash == legacyHash)
            && x.ExpiresAt > now
          )
          .OrderByDescending(x => x.RequestHash == hash)
          .ThenByDescending(x => x.CreatedAt)
          .FirstOrDefaultAsync(ct);
        PerformanceStages.Elapsed("routing", "reservation-cache-read", looking);
        if (cached?.ResultJson is { } cachedJson)
          return readCached(cachedJson, cached.RequestHash == hash);
        if (cached is not null)
          throw new RoutePlanningException(
            cached.ErrorMessage ?? "This route request is waiting to retry.",
            cached.ExpiresAt
          );
        var dayStart = now.Date;
        var minuteStart = now.AddMinutes(-1);
        var dailyLimit = Math.Clamp(
          configuration.GetValue("TomTom:DailyRequestLimit", 200),
          1,
          10000
        );
        // Counted into locals so each round trip can be timed on its own. The
        // queries, their order and the limits they enforce are unchanged.
        var counting = Stopwatch.GetTimestamp();
        var spentToday = await db.RoutingApiCalls.CountAsync(
          x => x.CreatedAt >= dayStart,
          ct
        );
        PerformanceStages.Elapsed("routing", "reservation-limit-day", counting);
        if (spentToday >= dailyLimit)
          throw new RoutePlanningException(
            "The daily TomTom request limit has been reached. Saved routes remain available.",
            now.Date.AddDays(1)
          );
        counting = Stopwatch.GetTimestamp();
        var spentThisMinute = await db.RoutingApiCalls.CountAsync(
          x => x.CreatedAt >= minuteStart,
          ct
        );
        PerformanceStages.Elapsed(
          "routing",
          "reservation-limit-minute",
          counting
        );
        if (
          spentThisMinute
          >= Math.Clamp(
            configuration.GetValue("TomTom:RequestsPerMinute", 30),
            1,
            100
          )
        )
          throw new RoutePlanningException(
            "Routing is busy. Wait a minute before trying again.",
            now.AddMinutes(1)
          );
        call = new RoutingApiCall
        {
          Id = Guid.NewGuid(),
          RequestHash = hash,
          Operation = operation,
          CreatedAt = now,
          ExpiresAt = now.AddMinutes(1),
          ErrorMessage = "This route request is waiting to retry.",
        };
        db.RoutingApiCalls.Add(call);
        var writing = Stopwatch.GetTimestamp();
        await db.SaveChangesAsync(ct);
        PerformanceStages.Elapsed("routing", "reservation-write", writing);
        // The committed reservation survives cancellation or process loss after
        // dispatch.
        var committing = Stopwatch.GetTimestamp();
        await transaction.CommitAsync(ct);
        PerformanceStages.Elapsed("routing", "reservation-commit", committing);
      }
      finally
      {
        ReservationGate.Release();
      }
      T result;
      try
      {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        if (http.Timeout != Timeout.InfiniteTimeSpan)
          timeout.CancelAfter(http.Timeout);
        using (PerformanceStages.Start("routing", "provider-body"))
        using (
          var response = await http.GetAsync(
            "https://api.tomtom.com/"
              + query
              + "&key="
              + Uri.EscapeDataString(configuration["TomTom:ApiKey"]!),
            HttpCompletionOption.ResponseHeadersRead,
            timeout.Token
          )
        )
        {
          if (!response.IsSuccessStatusCode)
            throw TomTomFailure.From(response.StatusCode, now);
          if (response.Content.Headers.ContentLength > MaximumResponseBytes)
            throw ResponseTooLarge();
          await using var stream = new BoundedResponseStream(
            await response.Content.ReadAsStreamAsync(timeout.Token)
          );
          using var json = await JsonDocument.ParseAsync(
            stream,
            cancellationToken: timeout.Token
          );
          result = parse(json.RootElement);
        }
        // Leg geometry is authoritative; the flat display path is rebuilt on
        // cache reads.
        call.ResultJson = serialize(result);
        call.ErrorMessage = null;
        call.ExpiresAt = now.Add(lifetime);
      }
      catch (OperationCanceledException) when (ct.IsCancellationRequested)
      {
        call.ErrorMessage =
          "The route request was cancelled. Please retry shortly.";
        await db.SaveChangesAsync(CancellationToken.None);
        throw;
      }
      catch (Exception ex)
        when (ex
            is HttpRequestException
              or JsonException
              or OperationCanceledException
              or RoutePlanningException
        )
      {
        var failure =
          ex as RoutePlanningException
          ?? new RoutePlanningException(
            "TomTom did not return a valid response. Please retry shortly.",
            now.AddMinutes(5)
          );
        call.ErrorMessage = failure.Message;
        call.ExpiresAt = failure.RetryAfter;
        await db.SaveChangesAsync(CancellationToken.None);
        throw failure;
      }
      var saving = Stopwatch.GetTimestamp();
      await db.SaveChangesAsync(ct);
      PerformanceStages.Elapsed("routing", "result-save", saving);
      return result;
    }
    finally
    {
      if (call is not null)
      {
        db.Entry(call).State = EntityState.Detached;
        call.ResultJson = null;
      }
    }
  }
}
