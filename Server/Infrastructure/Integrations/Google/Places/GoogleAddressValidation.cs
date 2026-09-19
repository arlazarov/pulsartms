using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using Application.Features.Routing.Exceptions;
using Application.Features.Routing.Models;

namespace Infrastructure.Integrations.Google.Places;

internal static class GoogleAddressValidation
{
  internal static async Task<RoutePoint> ResolveAsync(
    HttpClient http,
    string key,
    string input,
    JsonElement candidate,
    CancellationToken ct
  )
  {
    using var response = await http.PostAsJsonAsync(
      "https://addressvalidation.googleapis.com/v1:validateAddress?key="
        + Uri.EscapeDataString(key),
      new { address = new { addressLines = new[] { input } } },
      ct
    );
    if (!response.IsSuccessStatusCode)
      throw new RoutePlanningException(
        "Address validation is temporarily unavailable. The saved route has been kept.",
        DateTime.UtcNow.AddMinutes(5)
      );
    using var json = JsonDocument.Parse(
      await response.Content.ReadAsStringAsync(ct)
    );
    if (!json.RootElement.TryGetProperty("result", out var result))
      throw Unconfirmed();
    var verdict = result.GetProperty("verdict");
    if (
      !verdict.TryGetProperty("addressComplete", out var complete)
      || !complete.GetBoolean()
      || !verdict.TryGetProperty("possibleNextAction", out var action)
      || action.GetString() != "ACCEPT"
      || verdict.GetProperty("validationGranularity").GetString()
        is not ("PREMISE" or "SUB_PREMISE")
    )
      throw Unconfirmed();
    var address = result.GetProperty("address");
    var components = address
      .GetProperty("addressComponents")
      .EnumerateArray()
      .ToArray();
    string Confirmed(string type)
    {
      var matches = components
        .Where(x => x.GetProperty("componentType").GetString() == type)
        .ToArray();
      if (matches.Length != 1)
        throw Unconfirmed();
      var component = matches[0];
      if (
        component.GetProperty("confirmationLevel").GetString() != "CONFIRMED"
        || new[] { "inferred", "replaced", "unexpected" }.Any(flag =>
          component.TryGetProperty(flag, out var value) && value.GetBoolean()
        )
      )
        throw Unconfirmed();
      return component
          .GetProperty("componentName")
          .GetProperty("text")
          .GetString() ?? "";
    }
    var number = Confirmed("street_number");
    var street = Confirmed("route");
    var city = Confirmed("locality");
    var region = Confirmed("administrative_area_level_1");
    var country = address
      .GetProperty("postalAddress")
      .GetProperty("regionCode")
      .GetString();
    var postal = components.Any(x =>
      x.GetProperty("componentType").GetString() == "postal_code"
    )
      ? Confirmed("postal_code")
      : "";
    if (
      country is not ("US" or "CA")
      || number != Regex.Match(input.TrimStart(), @"^\d+[A-Za-z]?").Value
      || Regex.IsMatch(
        input,
        @"\b(?:USA|UNITED STATES)\b",
        RegexOptions.IgnoreCase
      )
        && country != "US"
      || Regex.IsMatch(input, @"\bCANADA\b", RegexOptions.IgnoreCase)
        && country != "CA"
      || !GoogleAddressGeocoder.MatchesLocality(
        input,
        city,
        country ?? "",
        postal
      )
      || !GoogleAddressGeocoder.MatchesRegion(input, region, country ?? "")
    )
      throw Unconfirmed();
    var parts = candidate
      .GetProperty("address_components")
      .EnumerateArray()
      .ToArray();
    string Part(string type) =>
      parts
        .Single(x =>
          x.GetProperty("types")
            .EnumerateArray()
            .Any(t => t.GetString() == type)
        )
        .GetProperty("short_name")
        .GetString() ?? "";
    var candidateLocation = candidate
      .GetProperty("geometry")
      .GetProperty("location");
    var validationLocation = result
      .GetProperty("geocode")
      .GetProperty("location");
    var samePremise =
      verdict.GetProperty("geocodeGranularity").GetString()
        is "PREMISE"
          or "SUB_PREMISE"
      && Math.Abs(
        candidateLocation.GetProperty("lat").GetDouble()
          - validationLocation.GetProperty("latitude").GetDouble()
      ) < 0.0001
      && Math.Abs(
        candidateLocation.GetProperty("lng").GetDouble()
          - validationLocation.GetProperty("longitude").GetDouble()
      ) < 0.0001;
    if (
      Part("street_number") != number
      || (
        !samePremise
        && GoogleAddressGeocoder.Normalize(Part("route"))
          != GoogleAddressGeocoder.Normalize(street)
      )
      || GoogleAddressGeocoder.City(Part("locality"))
        != GoogleAddressGeocoder.City(city)
      || Part("administrative_area_level_1") != region
      || Part("country") != country
    )
      throw Unconfirmed();
    RoutePoint point;
    if (
      verdict.GetProperty("geocodeGranularity").GetString()
      is "PREMISE"
        or "SUB_PREMISE"
    )
    {
      var location = result.GetProperty("geocode").GetProperty("location");
      point = new(
        location.GetProperty("latitude").GetDouble(),
        location.GetProperty("longitude").GetDouble()
      );
    }
    else
    {
      // Validation may confirm the street but not a warehouse unit. Keep only a
      // matching rooftop candidate.
      var geometry = candidate.GetProperty("geometry");
      if (geometry.GetProperty("location_type").GetString() != "ROOFTOP")
        throw Unconfirmed();
      var location = geometry.GetProperty("location");
      point = new(
        location.GetProperty("lat").GetDouble(),
        location.GetProperty("lng").GetDouble()
      );
    }
    return point.IsValid ? point : throw Unconfirmed();
  }

  private static RoutePlanningException Unconfirmed() =>
    new(
      "The address correction needs confirmation of the street and building number; no city-center fallback was used."
    );
}
