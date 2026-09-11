using Application.Features.Routing.Exceptions;
using Application.Features.Routing.Interfaces;
using System.Globalization;
using System.Buffers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Application.Interfaces;
using Domain.Entities.Dispatch;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Application.Features.Routing.Models;

namespace Infrastructure.Integrations.TomTom;

public sealed class TomTomRoutingProvider(HttpClient http, IConfiguration configuration, IAppDbContext db, IRouteRequestValidator validator, IAddressGeocoder geocoder, IRouteSectionValidator sectionsValidator) : IRoutingProvider
{
  private static readonly SemaphoreSlim Gate = new(1);
  private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
  private const int MaximumResponseBytes = 16 * 1024 * 1024;
  private const int MaximumCacheBytes = 32 * 1024 * 1024;
  private const int MaximumRoutePoints = 200_000;
  public bool IsConfigured => !string.IsNullOrWhiteSpace(configuration["TomTom:ApiKey"]);
  private static string Number(double v) => v.ToString("0.######", CultureInfo.InvariantCulture);

  public async Task<TruckRoute> CalculateAsync(IReadOnlyList<RoutePoint> points, TruckRouteProfile p, CancellationToken ct)
  {
    validator.Validate(points, p);
    var path = string.Join(":", points.Select(x => $"{Number(x.Latitude)},{Number(x.Longitude)}"));
    var query = $"routing/1/calculateRoute/{path}/json?travelMode=truck&vehicleCommercial=true&routeType=fastest"
      + $"&traffic=true&maxAlternatives=0&sectionType=travelMode&report=effectiveSettings"
      + $"&vehicleHeight={Number(p.HeightFeet * .3048)}&vehicleWidth={Number(p.WidthFeet * .3048)}"
      + $"&vehicleLength={Number(p.LengthFeet * .3048)}&vehicleWeight={Math.Ceiling(p.WeightPounds * .45359237)}"
      + $"&vehicleAxleWeight={Math.Ceiling(p.AxleWeightPounds * .45359237)}&vehicleNumberOfAxles={p.Axles}";
    if (p.Hazmat != "") query += $"&vehicleLoadType={Uri.EscapeDataString(p.Hazmat)}";
    var route = await CachedAsync("route", query, TimeSpan.FromHours(12), root => ParseRoute(root, points.Count - 1), ct);
    return CanonicalTiming(route);
  }

  private TruckRoute ParseRoute(JsonElement root, int expectedLegs)
  {
    try
    {
      if (!root.TryGetProperty("routes", out var routes))
        throw new RoutePlanningException("TomTom returned an incomplete route response. Please retry shortly.", DateTime.UtcNow.AddMinutes(5));
      if (routes.GetArrayLength() == 0)
        throw new RoutePlanningException("TomTom could not find a truck route for these stops.");
      var source = routes[0];
      var summary = source.GetProperty("summary");
      var legs = source.GetProperty("legs");
      if (legs.GetArrayLength() != expectedLegs)
        throw new RoutePlanningException("TomTom returned an incomplete route.", DateTime.UtcNow.AddMinutes(5));
      long pointCount = 0;
      foreach (var leg in legs.EnumerateArray())
      {
        pointCount += leg.GetProperty("points").GetArrayLength();
        if (pointCount > MaximumRoutePoints)
          throw new RoutePlanningException("TomTom returned more route geometry than can be processed safely. The saved plan has been kept.", DateTime.UtcNow.AddMinutes(5));
      }
      var result = new TruckRoute { Miles = summary.GetProperty("lengthInMeters").GetDouble() / 1609.344,
        Seconds = summary.GetProperty("travelTimeInSeconds").GetDouble() };
      if (!double.IsFinite(result.Miles) || result.Miles <= 0 || !double.IsFinite(result.Seconds) || result.Seconds < 0)
        throw new RoutePlanningException("TomTom returned invalid route metrics.", DateTime.UtcNow.AddMinutes(5));
      foreach (var leg in legs.EnumerateArray())
      {
        var ls = leg.GetProperty("summary");
        var lp = leg.GetProperty("points").EnumerateArray().Select(x => new RoutePoint(
          x.GetProperty("latitude").GetDouble(), x.GetProperty("longitude").GetDouble())).ToList();
        if (lp.Count < 2 || lp.Any(x => !x.IsValid)) throw new RoutePlanningException("TomTom returned incomplete route geometry.", DateTime.UtcNow.AddMinutes(5));
        var miles = ls.GetProperty("lengthInMeters").GetDouble() / 1609.344;
        var seconds = ls.GetProperty("travelTimeInSeconds").GetDouble();
        if (!double.IsFinite(miles) || miles < 0 || !double.IsFinite(seconds) || seconds < 0)
          throw new RoutePlanningException("TomTom returned invalid route metrics.", DateTime.UtcNow.AddMinutes(5));
        result.Legs.Add(new(miles, seconds, lp));
        result.Points.AddRange(result.Points.Count == 0 ? lp : lp.Skip(1));
      }
      if (source.TryGetProperty("sections", out var sections)) sectionsValidator.Validate(result, TomTomRouteSections.Read(sections));
      return CanonicalTiming(result);
    }
    catch (Exception ex) when (ex is KeyNotFoundException or InvalidOperationException or FormatException or OverflowException)
    {
      throw new RoutePlanningException("TomTom returned an incomplete or invalid route response. Please retry shortly.", DateTime.UtcNow.AddMinutes(5));
    }
  }

  private static TruckRoute CanonicalTiming(TruckRoute route)
  {
    if (!route.TryGetLegSeconds(out var seconds))
      throw new RoutePlanningException("TomTom returned inconsistent route timing. The saved route has been kept.", DateTime.UtcNow.AddMinutes(5));
    route.Seconds = seconds;
    return route;
  }

  public Task<RoutePoint> GeocodeAsync(string address, CancellationToken ct) =>
    geocoder.GeocodeAsync(address, ct);

  private static TruckRoute ReadCachedRoute(string json, bool compact)
  {
    if (json.Length > MaximumCacheBytes) throw InvalidCachedRoute();
    var bytes = Encoding.UTF8.GetByteCount(json);
    if (bytes > MaximumCacheBytes) throw InvalidCachedRoute();
    var buffer = ArrayPool<byte>.Shared.Rent(bytes);
    TruckRoute route;
    try
    {
      Encoding.UTF8.GetBytes(json, buffer);
      var utf8 = buffer.AsSpan(0, bytes);
      ValidateCachedPointCounts(utf8);
      route = JsonSerializer.Deserialize<TruckRoute>(utf8, Json) ?? throw InvalidCachedRoute();
    }
    catch (JsonException) { throw InvalidCachedRoute(); }
    finally { ArrayPool<byte>.Shared.Return(buffer); }
    if (route.Points is null || route.Legs is not { Count: > 0 } || route.Warnings is null
      || !double.IsFinite(route.Miles) || route.Miles <= 0
      || route.Points.Any(point => point?.IsValid != true)
      || route.Legs.Any(leg => leg is null || !double.IsFinite(leg.Miles) || leg.Miles < 0
        || leg.Points is not { Count: >= 2 } || leg.Points.Any(point => point?.IsValid != true)))
      throw InvalidCachedRoute();
    if (compact && route.Points.Count == 0)
      foreach (var leg in route.Legs)
        route.Points.AddRange(route.Points.Count == 0 ? leg.Points : leg.Points.Skip(1));
    if (route.Points.Count < 2) throw InvalidCachedRoute();
    return CanonicalTiming(route);
  }

  private static void ValidateCachedPointCounts(ReadOnlySpan<byte> json)
  {
    var reader = new Utf8JsonReader(json);
    if (!reader.Read() || reader.TokenType != JsonTokenType.StartObject) throw InvalidCachedRoute();
    var flatPoints = 0;
    var legPoints = 0;
    while (reader.Read() && reader.TokenType != JsonTokenType.EndObject)
    {
      var legs = Property(ref reader, "legs");
      var points = Property(ref reader, "points");
      if (!reader.Read()) throw InvalidCachedRoute();
      if (legs)
      {
        if (reader.TokenType != JsonTokenType.StartArray) throw InvalidCachedRoute();
        while (reader.Read() && reader.TokenType != JsonTokenType.EndArray)
        {
          if (reader.TokenType != JsonTokenType.StartObject) throw InvalidCachedRoute();
          var before = legPoints;
          while (reader.Read() && reader.TokenType != JsonTokenType.EndObject)
          {
            var coordinates = Property(ref reader, "points");
            if (!reader.Read()) throw InvalidCachedRoute();
            if (coordinates) CountPoints(ref reader, ref legPoints);
            else reader.Skip();
          }
          if (legPoints - before < 2) throw InvalidCachedRoute();
        }
      }
      else if (points) CountPoints(ref reader, ref flatPoints);
      else reader.Skip();
    }
    if (reader.Read()) throw InvalidCachedRoute();
  }

  private static bool Property(ref Utf8JsonReader reader, string name)
  {
    if (reader.TokenType != JsonTokenType.PropertyName) throw InvalidCachedRoute();
    return reader.ValueSpan.Length <= 128 && string.Equals(reader.GetString(), name, StringComparison.OrdinalIgnoreCase);
  }

  private static void CountPoints(ref Utf8JsonReader reader, ref int total)
  {
    if (reader.TokenType != JsonTokenType.StartArray) throw InvalidCachedRoute();
    while (reader.Read() && reader.TokenType != JsonTokenType.EndArray)
    {
      if (++total > MaximumRoutePoints || reader.TokenType != JsonTokenType.StartObject) throw InvalidCachedRoute();
      reader.Skip();
    }
  }

  private static RoutePlanningException InvalidCachedRoute() => new(
    "The saved TomTom route is incomplete or exceeds safe geometry limits. The saved plan has been kept.", DateTime.UtcNow.AddMinutes(5));

  private async Task<TruckRoute> CachedAsync(string operation, string query, TimeSpan lifetime, Func<JsonElement, TruckRoute> parse, CancellationToken ct)
  {
    if (!IsConfigured) throw new RoutePlanningException("TomTom API key is not configured on the server.");
    var legacyHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(query)));
    // Rolling or rolled-back binaries must not read the legs-only cache as a full route.
    var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes("route-legs-v1:" + query)));
    var cachedAt = DateTime.UtcNow;
    var existing = await db.RoutingApiCalls.AsNoTracking()
      .Where(x => (x.RequestHash == hash || x.RequestHash == legacyHash) && x.ExpiresAt > cachedAt)
      .OrderByDescending(x => x.RequestHash == hash).ThenByDescending(x => x.CreatedAt).FirstOrDefaultAsync(ct);
    if (existing?.ResultJson is { } existingJson) return ReadCachedRoute(existingJson, existing.RequestHash == hash);
    if (existing is not null) throw new RoutePlanningException(existing.ErrorMessage ?? "This route request is waiting to retry.", existing.ExpiresAt);
    await Gate.WaitAsync(ct);
    RoutingApiCall? call = null;
    try
    {
      DateTime now;
      await using (var transaction = await db.Database.BeginTransactionAsync(ct))
      {
        if (db.Database.IsNpgsql())
          await db.Database.ExecuteSqlRawAsync("SELECT pg_advisory_xact_lock(710246710)", ct);
        now = DateTime.UtcNow;
        var cached = await db.RoutingApiCalls.AsNoTracking().Where(x => (x.RequestHash == hash || x.RequestHash == legacyHash) && x.ExpiresAt > now)
          .OrderByDescending(x => x.RequestHash == hash).ThenByDescending(x => x.CreatedAt).FirstOrDefaultAsync(ct);
        if (cached?.ResultJson is { } cachedJson) return ReadCachedRoute(cachedJson, cached.RequestHash == hash);
        if (cached is not null) throw new RoutePlanningException(cached.ErrorMessage ?? "This route request is waiting to retry.", cached.ExpiresAt);
        var dayStart = now.Date;
        var minuteStart = now.AddMinutes(-1);
        var dailyLimit = Math.Clamp(configuration.GetValue("TomTom:DailyRequestLimit", 200), 1, 10000);
        if (await db.RoutingApiCalls.CountAsync(x => x.CreatedAt >= dayStart, ct) >= dailyLimit)
          throw new RoutePlanningException("The daily TomTom request limit has been reached. Saved routes remain available.", now.Date.AddDays(1));
        if (await db.RoutingApiCalls.CountAsync(x => x.CreatedAt >= minuteStart, ct) >= Math.Clamp(configuration.GetValue("TomTom:RequestsPerMinute", 30), 1, 100))
          throw new RoutePlanningException("Routing is busy. Wait a minute before trying again.", now.AddMinutes(1));
        call = new RoutingApiCall { Id = Guid.NewGuid(), RequestHash = hash, Operation = operation,
          CreatedAt = now, ExpiresAt = now.AddMinutes(1), ErrorMessage = "This route request is waiting to retry." };
        db.RoutingApiCalls.Add(call);
        await db.SaveChangesAsync(ct);
        // The committed reservation survives cancellation or process loss after dispatch.
        await transaction.CommitAsync(ct);
      }
      TruckRoute result;
      try
      {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        if (http.Timeout != Timeout.InfiniteTimeSpan) timeout.CancelAfter(http.Timeout);
        using (var response = await http.GetAsync("https://api.tomtom.com/" + query
          + "&key=" + Uri.EscapeDataString(configuration["TomTom:ApiKey"]!), HttpCompletionOption.ResponseHeadersRead, timeout.Token))
        {
          if (!response.IsSuccessStatusCode)
            throw new RoutePlanningException(response.StatusCode is System.Net.HttpStatusCode.Unauthorized or System.Net.HttpStatusCode.Forbidden
              ? "TomTom rejected this key or Routing is not enabled for this key."
              : $"TomTom request failed (HTTP {(int)response.StatusCode}). The saved plan has been kept.",
              response.StatusCode is System.Net.HttpStatusCode.Unauthorized or System.Net.HttpStatusCode.Forbidden
                ? now.AddHours(1)
                : (int)response.StatusCode >= 500 || response.StatusCode is System.Net.HttpStatusCode.TooManyRequests or System.Net.HttpStatusCode.RequestTimeout
                  ? now.AddMinutes(5) : null);
          if (response.Content.Headers.ContentLength > MaximumResponseBytes) throw ResponseTooLarge();
          await using var stream = new BoundedResponseStream(await response.Content.ReadAsStreamAsync(timeout.Token));
          using var json = await JsonDocument.ParseAsync(stream, cancellationToken: timeout.Token);
          result = parse(json.RootElement);
        }
        // Leg geometry is authoritative; the flat display path is rebuilt on cache reads.
        call.ResultJson = JsonSerializer.Serialize(new { result.CalculatedAt, result.Miles, result.Seconds,
          result.Legs, result.Warnings }, Json);
        call.ErrorMessage = null;
        call.ExpiresAt = now.Add(lifetime);
      }
      catch (OperationCanceledException) when (ct.IsCancellationRequested)
      {
        call.ErrorMessage = "The route request was cancelled. Please retry shortly.";
        await db.SaveChangesAsync(CancellationToken.None);
        throw;
      }
      catch (Exception ex) when (ex is HttpRequestException or JsonException or OperationCanceledException or RoutePlanningException)
      {
        var failure = ex as RoutePlanningException ?? new RoutePlanningException("TomTom did not return a valid response. Please retry shortly.", now.AddMinutes(5));
        call.ErrorMessage = failure.Message;
        call.ExpiresAt = failure.RetryAfter;
        await db.SaveChangesAsync(CancellationToken.None);
        throw failure;
      }
      await db.SaveChangesAsync(ct);
      return result;
    }
    finally
    {
      try
      {
        if (call is not null)
        {
          db.Entry(call).State = EntityState.Detached;
          call.ResultJson = null;
        }
      }
      finally { Gate.Release(); }
    }
  }

  private static RoutePlanningException ResponseTooLarge() => new(
    "TomTom returned a route response that is too large to process safely. The saved plan has been kept.", DateTime.UtcNow.AddMinutes(5));

  private sealed class BoundedResponseStream(Stream inner) : Stream
  {
    private int read;
    public override bool CanRead => true;
    public override bool CanSeek => false;
    public override bool CanWrite => false;
    public override long Length => throw new NotSupportedException();
    public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
    public override int Read(byte[] buffer, int offset, int count) =>
      Count(inner.Read(buffer, offset, Math.Min(count, MaximumResponseBytes - read + 1)));
    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken ct = default) =>
      Count(await inner.ReadAsync(buffer[..Math.Min(buffer.Length, MaximumResponseBytes - read + 1)], ct));
    private int Count(int count)
    {
      read += count;
      if (read > MaximumResponseBytes) throw ResponseTooLarge();
      return count;
    }
    protected override void Dispose(bool disposing) { if (disposing) inner.Dispose(); base.Dispose(disposing); }
    public override ValueTask DisposeAsync() => inner.DisposeAsync();
    public override void Flush() => throw new NotSupportedException();
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
  }
}
