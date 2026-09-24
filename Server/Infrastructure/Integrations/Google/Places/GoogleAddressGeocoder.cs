using System.Text.Json;
using System.Text.RegularExpressions;
using Application.Features.Routing.Interfaces;
using Domain.Models.Routing;
using Domain.Rules;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;

namespace Infrastructure.Integrations.Google.Places;

public sealed class GoogleAddressGeocoder(
  HttpClient http,
  IConfiguration configuration,
  IMemoryCache cache
) : IAddressGeocoder
{
  private static readonly SemaphoreSlim Gate = new(1, 1);

  private sealed record FailedLookup(string Message, DateTime RetryAfter);

  public async Task<RoutePoint> GeocodeAsync(
    string address,
    CancellationToken ct
  ) => (await ResolveAsync(address, ct)).Point;

  public async Task<ResolvedAddress> ResolveAsync(
    string address,
    CancellationToken ct
  )
  {
    address = NormalizeQuery(address);
    var key = configuration["GooglePlaces:ApiKey"];
    if (string.IsNullOrWhiteSpace(key))
      throw new RoutePlanningException(
        "Google address lookup is not configured."
      );
    var cacheKey = "stop-geocode:" + address.Trim().ToUpperInvariant();
    var failureKey = cacheKey + ":failure";
    await Gate.WaitAsync(ct);
    try
    {
      if (cache.TryGetValue<ResolvedAddress>(cacheKey, out var cached))
        return cached!;
      if (cache.TryGetValue<FailedLookup>(failureKey, out var failed))
        throw new RoutePlanningException(failed!.Message, failed.RetryAfter);
      using var response = await http.GetAsync(
        "https://maps.googleapis.com/maps/api/geocode/json?address="
          + Uri.EscapeDataString(address)
          + "&key="
          + Uri.EscapeDataString(key),
        ct
      );
      if (!response.IsSuccessStatusCode)
        throw new RoutePlanningException(
          $"Google address lookup failed (HTTP {(int)response.StatusCode}).",
          DateTime.UtcNow.AddMinutes(5)
        );
      using var json = JsonDocument.Parse(
        await response.Content.ReadAsStringAsync(ct)
      );
      var root = json.RootElement;
      if (root.GetProperty("status").GetString() != "OK")
        throw new RoutePlanningException(
          "Google could not resolve the stop address. Check the address and API configuration.",
          DateTime.UtcNow.AddHours(1)
        );
      var candidates = root.GetProperty("results").EnumerateArray().ToArray();
      var matches = candidates
        .Where(result => Matches(result, address))
        .ToList();
      if (matches.Count != 1 && candidates.Length != 1)
        throw new RoutePlanningException(
          "The stop needs an unambiguous street-level address; no city-center fallback was used."
        );
      RoutePoint point;
      if (matches.Count == 1)
      {
        var location = matches[0]
          .GetProperty("geometry")
          .GetProperty("location");
        point = new(
          location.GetProperty("lat").GetDouble(),
          location.GetProperty("lng").GetDouble()
        );
      }
      else
        point = await GoogleAddressValidation.ResolveAsync(
          http,
          key,
          address,
          candidates[0],
          ct
        );
      if (!point.IsValid)
        throw new RoutePlanningException(
          "Google returned invalid stop coordinates."
        );
      var candidate = matches.Count == 1 ? matches[0] : candidates[0];
      var parts = candidate
        .GetProperty("address_components")
        .EnumerateArray()
        .ToArray();
      string Part(string type) =>
        parts.FirstOrDefault(c =>
          c.GetProperty("types")
            .EnumerateArray()
            .Any(t => t.GetString() == type)
        )
          is var part
        && part.ValueKind != JsonValueKind.Undefined
          ? part.GetProperty("short_name").GetString() ?? ""
          : "";
      var street = string.Join(
        " ",
        new[] { Part("street_number"), Part("route") }.Where(x => x.Length > 0)
      );
      if (Part("subpremise") is { Length: > 0 } unit)
        street += ", " + unit;
      var resolved = new ResolvedAddress(
        point,
        street,
        Part("locality") is { Length: > 0 } city ? city : Part("postal_town"),
        Part("administrative_area_level_1"),
        Part("country"),
        Part("postal_code")
      );
      cache.Set(cacheKey, resolved, TimeSpan.FromHours(12));
      return resolved;
    }
    catch (OperationCanceledException) when (ct.IsCancellationRequested)
    {
      throw;
    }
    catch (RoutePlanningException ex)
    {
      // Share failures across route consumers without storing provider response
      // data.
      var expiry =
        ex.RetryAfter < DateTime.UtcNow.AddHours(1)
          ? ex.RetryAfter
          : DateTime.UtcNow.AddHours(1);
      if (expiry > DateTime.UtcNow && !cache.TryGetValue(failureKey, out _))
        cache.Set(
          failureKey,
          new FailedLookup(ex.Message, ex.RetryAfter),
          expiry
        );
      throw;
    }
    catch (Exception ex)
      when (ex
          is HttpRequestException
            or JsonException
            or OperationCanceledException
            or KeyNotFoundException
            or InvalidOperationException
      )
    {
      var failure = new FailedLookup(
        "Google address lookup is temporarily unavailable. The saved route has been kept.",
        DateTime.UtcNow.AddMinutes(5)
      );
      cache.Set(failureKey, failure, failure.RetryAfter);
      throw new RoutePlanningException(failure.Message, failure.RetryAfter);
    }
    finally
    {
      Gate.Release();
    }
  }

  private static bool Matches(JsonElement result, string address)
  {
    if (
      result.TryGetProperty("partial_match", out var partial)
      && partial.GetBoolean()
    )
      return false;
    var precision = result
      .GetProperty("geometry")
      .GetProperty("location_type")
      .GetString();
    if (precision is not ("ROOFTOP" or "RANGE_INTERPOLATED"))
      return false;
    var components = result
      .GetProperty("address_components")
      .EnumerateArray()
      .ToList();
    string Component(string type) =>
      components.FirstOrDefault(c =>
        c.GetProperty("types").EnumerateArray().Any(t => t.GetString() == type)
      )
        is var component
      && component.ValueKind != JsonValueKind.Undefined
        ? component.GetProperty("short_name").GetString() ?? ""
        : "";
    var number = Component("street_number");
    var route = Component("route");
    var country = Component("country");
    if (
      number.Length == 0
      || route.Length == 0
      || country is not ("US" or "CA")
    )
      return false;
    var region = Component("administrative_area_level_1");
    var city = Component("locality");
    if (city.Length == 0)
      city = Component("postal_town");
    if (
      !GoogleAddressMatching.MatchesRegion(address, region, country)
      || !GoogleAddressMatching.MatchesLocality(
        address,
        city,
        country,
        Component("postal_code")
      )
    )
      return false;
    return GoogleAddressMatching.Street(address.Split(',')[0], region)
      == GoogleAddressMatching.Street(number + " " + route, region);
  }

  private static string NormalizeQuery(string address)
  {
    address = Regex.Replace(
      address,
      @"\s+\bATTN\b\.?\s*:?[^,]*",
      "",
      RegexOptions.IgnoreCase
    );
    address = Regex.Replace(
      address,
      @",\s*(?:DOOR|DOCK)\s*(?:#\s*)?[A-Z0-9-]+\b",
      "",
      RegexOptions.IgnoreCase
    );
    address = Regex.Replace(
      address,
      @"(^|,)\s*CAN\s*(?=,|$)",
      "$1 Canada",
      RegexOptions.IgnoreCase
    );
    var locality = address.IndexOf(',');
    // Format a US ZIP+4 without interpreting a street/building number as a
    // postal code.
    if (
      locality >= 0
      && Regex.IsMatch(
        address[locality..],
        @"\b(?:US|USA|UNITED STATES)\b",
        RegexOptions.IgnoreCase
      )
    )
      address =
        address[..locality]
        + Regex.Replace(address[locality..], @"\b(\d{5})(\d{4})\b", "$1-$2");
    return address;
  }
}
