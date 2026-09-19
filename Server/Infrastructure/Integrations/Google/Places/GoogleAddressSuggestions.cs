using System.Net.Http.Json;
using System.Text.Json;
using Application.Features.Addresses.Interfaces;
using Application.Features.Addresses.Models;
using Microsoft.Extensions.Configuration;

namespace Infrastructure.Integrations.Google.Places;

public sealed class GoogleAddressSuggestions(
  HttpClient http,
  IConfiguration configuration
) : IAddressSuggestionsProvider
{
  public async Task<AddressSuggestions> SuggestAsync(
    string query,
    Guid session,
    CancellationToken ct
  )
  {
    using var request = Request(HttpMethod.Post, "places:autocomplete");
    if (request is null)
      return new([], true);
    request.Content = JsonContent.Create(
      new
      {
        input = query,
        sessionToken = session.ToString(),
        includedRegionCodes = new[] { "us", "ca" },
        includeQueryPredictions = false,
        languageCode = "en",
      }
    );
    request.Headers.Add(
      "X-Goog-FieldMask",
      "suggestions.placePrediction.placeId,suggestions.placePrediction.text.text"
    );
    using var json = await SendAsync(request, ct);
    if (json is null)
      return new([], true);
    if (!json.RootElement.TryGetProperty("suggestions", out var items))
      return new([]);
    var result = new List<AddressSuggestion>();
    foreach (var item in items.EnumerateArray().Take(5))
    {
      if (!item.TryGetProperty("placePrediction", out var place))
        continue;
      var id = Text(place, "placeId");
      var label = place.TryGetProperty("text", out var text)
        ? Text(text, "text")
        : "";
      if (id.Length > 0 && label.Length > 0)
        result.Add(new(id, label));
    }
    var enriched = await Task.WhenAll(
      result.Select(async item =>
      {
        // Preview details must not terminate the user's autocomplete session.
        var address = await DetailsAsync(item.Id, null, ct);
        return item with { PostalCode = address?.PostalCode ?? "" };
      })
    );
    return new(enriched);
  }

  public Task<PostalAddress?> ResolveAsync(
    string id,
    Guid session,
    CancellationToken ct
  ) => DetailsAsync(id, session, ct);

  private async Task<PostalAddress?> DetailsAsync(
    string id,
    Guid? session,
    CancellationToken ct
  )
  {
    using var request = Request(
      HttpMethod.Get,
      "places/"
        + Uri.EscapeDataString(id)
        + "?languageCode=en"
        + (session.HasValue ? "&sessionToken=" + session : "")
    );
    if (request is null)
      return null;
    request.Headers.Add("X-Goog-FieldMask", "addressComponents");
    using var json = await SendAsync(request, ct);
    if (
      json is null
      || !json.RootElement.TryGetProperty(
        "addressComponents",
        out var components
      )
    )
      return null;
    string Part(string type, bool shortName = false)
    {
      foreach (var part in components.EnumerateArray())
        if (
          part.TryGetProperty("types", out var types)
          && types.EnumerateArray().Any(x => x.GetString() == type)
        )
          return Text(part, shortName ? "shortText" : "longText");
      return "";
    }
    var street = string.Join(
      " ",
      new[] { Part("street_number"), Part("route") }.Where(x => x.Length > 0)
    );
    var country = Part("country", true);
    if (street.Length == 0 || country is not ("CA" or "US"))
      return null;
    if (Part("subpremise") is { Length: > 0 } unit)
      street += ", " + unit;
    var postal = Part("postal_code");
    if (Part("postal_code_suffix") is { Length: > 0 } suffix)
      postal += "-" + suffix;
    return new(
      street,
      Part("locality") is { Length: > 0 } city ? city : Part("postal_town"),
      Part("administrative_area_level_1", true),
      country,
      postal
    );
  }

  private HttpRequestMessage? Request(HttpMethod method, string path)
  {
    var key = configuration["GooglePlaces:ApiKey"];
    if (string.IsNullOrWhiteSpace(key))
      return null;
    var request = new HttpRequestMessage(
      method,
      "https://places.googleapis.com/v1/" + path
    );
    request.Headers.Add("X-Goog-Api-Key", key);
    return request;
  }

  private async Task<JsonDocument?> SendAsync(
    HttpRequestMessage request,
    CancellationToken ct
  )
  {
    try
    {
      using var response = await http.SendAsync(request, ct);
      if (!response.IsSuccessStatusCode)
        return null;
      return JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
    }
    catch (OperationCanceledException) when (ct.IsCancellationRequested)
    {
      throw;
    }
    catch (Exception ex)
      when (ex
          is HttpRequestException
            or JsonException
            or OperationCanceledException
      )
    {
      return null;
    }
  }

  private static string Text(JsonElement item, string key) =>
    item.TryGetProperty(key, out var value) ? value.GetString() ?? "" : "";
}
